using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HOPPER.Infrastructure.Interfaces;

namespace HOPPER.Application.ModMetadata
{
    public static class ModIdReader
    {
        private const int MaxMetadataBytes = 1024 * 1024;

        private const string NeoForgeToml = "META-INF/neoforge.mods.toml";
        private const string ForgeToml = "META-INF/mods.toml";
        private const string FabricJson = "fabric.mod.json";
        private const string QuiltJson = "quilt.mod.json";
        private const string McmodInfo = "mcmod.info";

        private static readonly Regex ValidModId = new("^[a-z][a-z0-9_.-]{1,63}$", RegexOptions.CultureInvariant);

        public static bool IsValidModId(string id) => ValidModId.IsMatch(id);

        public static string[] Read(Stream seekableJar)
        {
            try
            {
                using var archive = new ZipArchive(seekableJar, ZipArchiveMode.Read, leaveOpen: true);

                var ids = new List<string>();

                var toml = Text(archive, NeoForgeToml);
                var tomlIds = toml is null ? [] : ModsTomlParser.Parse(toml);

                if (tomlIds.Length == 0)
                {
                    var legacy = Text(archive, ForgeToml);
                    if (legacy is not null)
                        tomlIds = ModsTomlParser.Parse(legacy);
                }

                Add(ids, tomlIds);

                var fabric = Text(archive, FabricJson);
                if (fabric is not null) Add(ids, FromFabricJson(fabric));

                var quilt = Text(archive, QuiltJson);
                if (quilt is not null) Add(ids, FromQuiltJson(quilt));

                var mcmod = Text(archive, McmodInfo);
                if (mcmod is not null) Add(ids, FromMcmodInfo(mcmod));

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
                return Read(stream);
        }

        public static string[] FromFabricJson(string text)
        {
            try
            {
                using var document = JsonDocument.Parse(text);

                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("id", out var id)
                    || id.ValueKind != JsonValueKind.String)
                {
                    return [];
                }

                return One(id.GetString());
            }
            catch (JsonException)
            {
                return [];
            }
        }

        public static string[] FromQuiltJson(string text)
        {
            try
            {
                using var document = JsonDocument.Parse(text);

                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("quilt_loader", out var loader)
                    || loader.ValueKind != JsonValueKind.Object
                    || !loader.TryGetProperty("id", out var id)
                    || id.ValueKind != JsonValueKind.String)
                {
                    return [];
                }

                return One(id.GetString());
            }
            catch (JsonException)
            {
                return [];
            }
        }

        public static string[] FromMcmodInfo(string text)
        {
            try
            {
                using var document = JsonDocument.Parse(text);
                var root = document.RootElement;

                JsonElement list;

                if (root.ValueKind == JsonValueKind.Array)
                {
                    list = root;
                }
                else if (root.ValueKind == JsonValueKind.Object
                         && root.TryGetProperty("modList", out var wrapped)
                         && wrapped.ValueKind == JsonValueKind.Array)
                {
                    list = wrapped;
                }
                else
                {
                    return [];
                }

                var ids = new List<string>();

                foreach (var element in list.EnumerateArray())
                {
                    if (element.ValueKind == JsonValueKind.Object
                        && element.TryGetProperty("modid", out var id)
                        && id.ValueKind == JsonValueKind.String)
                    {
                        Add(ids, One(id.GetString()));
                    }
                }

                return [.. ids];
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private static string? Text(ZipArchive archive, string name)
        {
            var entry = archive.GetEntry(name);
            if (entry is null)
                return null;

            if (entry.Length > MaxMetadataBytes)
                return null;

            using var stream = entry.Open();
            using var buffer = new MemoryStream();

            var chunk = new byte[8192];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                if (buffer.Length + read > MaxMetadataBytes)
                    return null;

                buffer.Write(chunk, 0, read);
            }

            var bytes = buffer.ToArray();

            var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;

            return Encoding.UTF8.GetString(bytes, offset, bytes.Length - offset);
        }

        private static string[] One(string? id) =>
            id is not null && IsValidModId(id) ? [id] : [];

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
