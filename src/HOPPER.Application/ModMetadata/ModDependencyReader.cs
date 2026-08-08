using System.IO.Compression;
using System.Text.Json;
using HOPPER.Application.Imports;
using HOPPER.Infrastructure.Interfaces;

namespace HOPPER.Application.ModMetadata
{
    /// The mod ids a jar says it cannot run without. Only the mandatory ones: an optional dependency
    /// that is absent is a working setup, not a broken one.
    public static class ModDependencyReader
    {
        private const int MaxMetadataBytes = 1024 * 1024;

        private const string NeoForgeToml = "META-INF/neoforge.mods.toml";
        private const string ForgeToml = "META-INF/mods.toml";
        private const string FabricJson = "fabric.mod.json";
        private const string QuiltJson = "quilt.mod.json";

        /// Supplied by the loader or the game itself, so a jar asking for one is never missing
        /// anything. Being wrong here reads as a broken server, so the list stays generous.
        private static readonly HashSet<string> AlwaysPresent = new(StringComparer.OrdinalIgnoreCase)
        {
            "minecraft", "java", "mcp",
            "forge", "neoforge", "fml", "javafml", "lowcodefml", "mclanguage",
            "fabricloader", "fabric-loader", "fabric_loader",
            "quilt_loader", "quilt_base", "quilted_fabric_loader", "quilt_loader_api",
            "mixinextras",
        };

        public static bool IsProvidedByTheLoader(string id) => AlwaysPresent.Contains(id);

        public static string[]? FromBlob(IBlobStorage blobs, string sha256)
        {
            Stream? stream;

            try
            {
                stream = blobs.OpenRead(sha256);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                return null;
            }

            if (stream is null)
                return null;

            using (stream)
            {
                try
                {
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                    return Read(archive);
                }
                catch (Exception ex) when (ex is InvalidDataException
                                              or IOException
                                              or NotSupportedException
                                              or ObjectDisposedException
                                              or ArgumentException)
                {
                    return [];
                }
            }
        }

        public static string[] Read(ZipArchive archive)
        {
            try
            {
                var ids = new List<string>();

                var toml = Text(archive, NeoForgeToml) ?? Text(archive, ForgeToml);
                if (toml is not null) Add(ids, FromModsToml(toml));

                var fabric = Text(archive, FabricJson);
                if (fabric is not null) Add(ids, FromJsonDepends(fabric, "depends"));

                var quilt = Text(archive, QuiltJson);
                if (quilt is not null) Add(ids, FromQuiltJson(quilt));

                return [.. ids];
            }
            catch (Exception ex) when (ex is InvalidDataException
                                          or IOException
                                          or NotSupportedException
                                          or ObjectDisposedException
                                          or ArgumentException)
            {
                return [];
            }
        }

        /// `depends` is an object keyed by mod id in both fabric.mod.json and quilt.mod.json's
        /// fabric half; the value is a version range nobody here needs.
        public static string[] FromJsonDepends(string text, string property)
        {
            try
            {
                using var document = JsonDocument.Parse(text);

                if (!document.RootElement.TryGetProperty(property, out var depends))
                    return [];

                return depends.ValueKind switch
                {
                    JsonValueKind.Object => [.. depends.EnumerateObject()
                        .Select(p => p.Name)
                        .Where(ModIdReader.IsValidModId)],
                    _ => [],
                };
            }
            catch (JsonException)
            {
                return [];
            }
        }

        /// Quilt nests under quilt_loader.depends, and each entry is either a bare id or an object
        /// with an `id` - and optional ones carry `optional: true`.
        public static string[] FromQuiltJson(string text)
        {
            try
            {
                using var document = JsonDocument.Parse(text);

                if (!document.RootElement.TryGetProperty("quilt_loader", out var loader)
                    || !loader.TryGetProperty("depends", out var depends)
                    || depends.ValueKind != JsonValueKind.Array)
                {
                    return [];
                }

                var ids = new List<string>();

                foreach (var entry in depends.EnumerateArray())
                {
                    var id = entry.ValueKind switch
                    {
                        JsonValueKind.String => entry.GetString(),
                        JsonValueKind.Object when Optional(entry) => null,
                        JsonValueKind.Object when entry.TryGetProperty("id", out var value) => value.GetString(),
                        _ => null,
                    };

                    // Quilt ids may be group-qualified as "group:id", and the loader matches on the id.
                    if (id is not null && id.LastIndexOf(':') is var colon && colon >= 0)
                        id = id[(colon + 1)..];

                    if (id is not null && ModIdReader.IsValidModId(id))
                        Add(ids, [id]);
                }

                return [.. ids];
            }
            catch (JsonException)
            {
                return [];
            }

            static bool Optional(JsonElement entry) =>
                entry.TryGetProperty("optional", out var optional)
                && optional.ValueKind == JsonValueKind.True;
        }

        /// `[[dependencies.<owner>]]` blocks, each with a modId and a mandatory flag. Written by hand
        /// rather than with a TOML parser for the same reason ModsTomlParser is.
        public static string[] FromModsToml(string text)
        {
            var ids = new List<string>();

            string? modId = null;
            bool? mandatory = null;

            void Flush()
            {
                if (modId is not null && mandatory != false && ModIdReader.IsValidModId(modId))
                    Add(ids, [modId]);

                modId = null;
                mandatory = null;
            }

            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();

                if (line.StartsWith("[[dependencies", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith("[[mods", StringComparison.OrdinalIgnoreCase)
                    || line.StartsWith('['))
                {
                    Flush();
                    continue;
                }

                if (Value(line, "modId") is { } id)
                    modId = id.Trim().ToLowerInvariant();
                else if (Value(line, "mandatory") is { } flag)
                    mandatory = flag.Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            Flush();

            return [.. ids];

            static string? Value(string line, string key)
            {
                if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    return null;

                var equals = line.IndexOf('=');
                if (equals < 0 || line[..equals].Trim().Length != key.Length)
                    return null;

                var value = line[(equals + 1)..].Trim();

                var comment = value.IndexOf('#');
                if (comment >= 0) value = value[..comment].Trim();

                return value.Trim('"', '\'');
            }
        }

        private static string? Text(ZipArchive archive, string name) =>
            ZipEntryText.Read(archive, name, MaxMetadataBytes);

        private static void Add(List<string> ids, string[] more)
        {
            foreach (var id in more)
            {
                if (!ids.Contains(id, StringComparer.Ordinal))
                    ids.Add(id);
            }
        }
    }
}
