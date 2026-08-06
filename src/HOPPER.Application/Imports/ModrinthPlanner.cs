using System.IO.Compression;
using System.Text.Json;
using HOPPER.Domain.Enums;

namespace HOPPER.Application.Imports
{
    /// <summary>Plans a .mrpack. This is the one format that needs no key and no human: every entry
    /// in modrinth.index.json carries direct, anonymous HTTPS URLs and mandatory sha1 + sha512, so a
    /// clean import produces zero pending rows.</summary>
    public static class ModrinthPlanner
    {
        public static PackPlan Plan(ZipArchive archive, string prefix)
        {
            var indexEntry = archive.GetEntry(prefix + "modrinth.index.json")
                ?? throw new PackImportException("modrinth.index.json is missing.");

            JsonDocument document;
            using (var stream = indexEntry.Open())
            {
                try
                {
                    document = JsonDocument.Parse(stream);
                }
                catch (JsonException ex)
                {
                    throw new PackImportException($"modrinth.index.json is not valid JSON: {ex.Message}");
                }
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

                var files = new List<PlannedFile>();
                var skipped = 0;

                if (root.TryGetProperty("files", out var entries) && entries.ValueKind == JsonValueKind.Array)
                {
                    foreach (var file in entries.EnumerateArray())
                    {
                        var path = file.TryGetProperty("path", out var p) ? p.GetString() : null;
                        if (string.IsNullOrWhiteSpace(path))
                            continue;

                        // HOPPER distributes mods. resourcepacks/, shaderpacks/ and datapacks/ are real
                        // entries in real packs (Better MC has 31 of them) and are counted as skipped
                        // rather than dropped silently, so a shrinking mod count is explainable.
                        if (!path.StartsWith("mods/", StringComparison.OrdinalIgnoreCase)
                            || !path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                        {
                            skipped++;
                            continue;
                        }

                        // env is optional in the spec even though every real pack carries it. Absent
                        // means "install everywhere"; only an explicit client:"unsupported" is a reason
                        // to leave it out, since what HOPPER feeds is game clients.
                        if (file.TryGetProperty("env", out var env)
                            && env.ValueKind == JsonValueKind.Object
                            && env.TryGetProperty("client", out var client)
                            && string.Equals(client.GetString(), "unsupported", StringComparison.OrdinalIgnoreCase))
                        {
                            skipped++;
                            continue;
                        }

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
                        });
                    }
                }

                // Not optional, and the easiest thing in this whole pipeline to forget: overrides/mods
                // is where the jars that are not hosted on Modrinth live - 21 of them in Better MC -
                // and a pack imported without them is a pack that does not launch.
                files.AddRange(OverrideJars(archive, prefix + "overrides/mods/"));
                files.AddRange(OverrideJars(archive, prefix + "client-overrides/mods/"));

                return new PackPlan { Format = PackFormat.Modrinth, Files = files, Skipped = skipped };
            }
        }

        /// <summary>server-overrides/ is deliberately not read: those files exist because they are
        /// wrong on a client, which is the only kind of machine HOPPER sends jars to.</summary>
        private static IEnumerable<PlannedFile> OverrideJars(ZipArchive archive, string folder) =>
            archive.Entries
                .Where(e => e.FullName.StartsWith(folder, StringComparison.OrdinalIgnoreCase) && PackDetector.IsJar(e))
                .Select(e => new PlannedFile { FileName = e.Name, ZipEntry = e.FullName });

        private static string BaseName(string path)
        {
            var slash = path.LastIndexOf('/');
            return slash < 0 ? path : path[(slash + 1)..];
        }
    }
}
