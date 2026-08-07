using System.IO.Compression;
using System.Text.Json;
using HOPPER.Application.Exports;
using HOPPER.Domain.Enums;

namespace HOPPER.Application.Imports
{
    public static class ModrinthPlanner
    {
        public static PackPlan Plan(ZipArchive archive, string prefix, PackPlanContext context)
        {
            var indexEntry = archive.GetEntry(prefix + "modrinth.index.json")
                ?? throw new PackImportException("modrinth.index.json is missing.");

            var text = ZipEntryText.Read(indexEntry, context.MaxMetadataBytes)
                ?? throw new PackImportException(
                    $"modrinth.index.json is larger than the {context.MaxMetadataBytes} byte limit. Raise Hopper:MaxPackMetadataBytes to accept it.");

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(text);
            }
            catch (JsonException ex)
            {
                throw new PackImportException($"modrinth.index.json is not valid JSON: {ex.Message}");
            }

            using (document)
            {
                var root = document.RootElement;

                if (root.TryGetProperty("formatVersion", out var formatVersion)
                    && formatVersion.ValueKind == JsonValueKind.Number
                    && formatVersion.GetInt32() != 1)
                {
                    throw new PackImportException($"Unsupported .mrpack formatVersion {formatVersion.GetInt32()}.");
                }

                if (root.TryGetProperty("game", out var game)
                    && game.ValueKind == JsonValueKind.String
                    && !string.Equals(game.GetString(), "minecraft", StringComparison.OrdinalIgnoreCase))
                {
                    throw new PackImportException($"This .mrpack is for {game.GetString()}, not Minecraft.");
                }

                var warnings = PackPlatformCheck.Verify(DeclaredPlatform(root), context.Target);

                var files = new List<PlannedFile>();
                var skipped = 0;

                if (root.TryGetProperty("files", out var entries) && entries.ValueKind == JsonValueKind.Array)
                {
                    foreach (var file in entries.EnumerateArray())
                    {
                        var path = file.TryGetProperty("path", out var p) ? p.GetString() : null;
                        if (string.IsNullOrWhiteSpace(path))
                            continue;

                        if (!path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)
                            || !path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                        {
                            skipped++;
                            continue;
                        }

                        var side = PackEnv.SideOf(file);

                        var downloads = new List<Uri>();
                        if (file.TryGetProperty("downloads", out var urls) && urls.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var url in urls.EnumerateArray())
                            {
                                if (Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri))
                                    downloads.Add(uri);
                            }
                        }

                        string? sha1 = null, sha512 = null;
                        if (file.TryGetProperty("hashes", out var hashes) && hashes.ValueKind == JsonValueKind.Object)
                        {
                            sha1 = hashes.TryGetProperty("sha1", out var h1) ? h1.GetString() : null;
                            sha512 = hashes.TryGetProperty("sha512", out var h5) ? h5.GetString() : null;
                        }

                        files.Add(new PlannedFile
                        {
                            FileName = BaseName(path),
                            Downloads = downloads,
                            Sha1 = sha1,
                            Sha512 = sha512,
                            Size = file.TryGetProperty("fileSize", out var size) && size.ValueKind == JsonValueKind.Number
                                ? size.GetInt64()
                                : null,
                            Side = side,
                        });
                    }
                }

                var overrides = new Dictionary<string, PlannedFile>(StringComparer.OrdinalIgnoreCase);

                foreach (var jar in OverrideJars(archive, prefix + "overrides/mods/", ModSide.Both))
                    overrides[jar.FileName] = jar;

                foreach (var jar in OverrideJars(archive, prefix + "client-overrides/mods/", ModSide.ClientOnly))
                    overrides[jar.FileName] = jar;

                foreach (var jar in OverrideJars(archive, prefix + "server-overrides/mods/", ModSide.ServerOnly))
                    overrides[jar.FileName] = jar;

                files.RemoveAll(f => overrides.ContainsKey(f.FileName));
                files.AddRange(overrides.Values);

                return new PackPlan
                {
                    Format = PackFormat.Modrinth,
                    Files = files,
                    Warnings = warnings,
                    Skipped = skipped,
                };
            }
        }

        private static PackPlatform DeclaredPlatform(JsonElement root)
        {
            if (!root.TryGetProperty("dependencies", out var dependencies)
                || dependencies.ValueKind != JsonValueKind.Object)
            {
                return PackPlatform.Unknown;
            }

            string? minecraft = null;
            var loader = ModLoader.Unknown;

            foreach (var dependency in dependencies.EnumerateObject())
            {
                if (dependency.Value.ValueKind != JsonValueKind.String)
                    continue;

                if (string.Equals(dependency.Name, "minecraft", StringComparison.OrdinalIgnoreCase))
                {
                    minecraft = dependency.Value.GetString();
                    continue;
                }

                var candidate = LoaderIds.FromMrpackKey(dependency.Name);
                if (candidate != ModLoader.Unknown)
                    loader = candidate;
            }

            return new PackPlatform(minecraft, loader);
        }

        private static IEnumerable<PlannedFile> OverrideJars(ZipArchive archive, string folder, ModSide side) =>
            archive.Entries
                .Where(e => e.FullName.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && PackDetector.IsJar(e))
                .Select(e => new PlannedFile { FileName = e.Name, ZipEntry = e.FullName, Side = side });

        private static string BaseName(string path)
        {
            var slash = path.LastIndexOf('/');
            return slash < 0 ? path : path[(slash + 1)..];
        }
    }
}
