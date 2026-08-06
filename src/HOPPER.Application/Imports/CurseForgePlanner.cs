using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using HOPPER.Domain.Enums;

namespace HOPPER.Application.Imports
{
    public static partial class CurseForgePlanner
    {
        public static async Task<PackPlan> PlanAsync(
            ZipArchive archive,
            string prefix,
            ICurseForgeClient curseForge,
            CancellationToken cancellationToken)
        {
            var manifestEntry = archive.GetEntry(prefix + "manifest.json")
                ?? throw new PackImportException("manifest.json is missing.");

            JsonDocument document;
            using (var stream = manifestEntry.Open())
            {
                try
                {
                    document = JsonDocument.Parse(stream);
                }
                catch (JsonException ex)
                {
                    throw new PackImportException($"manifest.json is not valid JSON: {ex.Message}");
                }
            }

            using (document)
            {
                var root = document.RootElement;

                if (!root.TryGetProperty("manifestType", out var type)
                    || !string.Equals(type.GetString(), "minecraftModpack", StringComparison.Ordinal))
                {
                    throw new PackImportException("manifest.json is not a Minecraft modpack manifest.");
                }

                if (root.TryGetProperty("manifestVersion", out var version)
                    && version.ValueKind == JsonValueKind.Number
                    && version.GetInt32() != 1)
                {
                    throw new PackImportException($"Unsupported CurseForge manifestVersion {version.GetInt32()}.");
                }

                var overrides = root.TryGetProperty("overrides", out var o) && o.ValueKind == JsonValueKind.String
                    ? o.GetString()
                    : "overrides";

                var files = archive.Entries
                    .Where(e => e.FullName.StartsWith($"{prefix}{overrides}/mods/", StringComparison.OrdinalIgnoreCase)
                                && PackDetector.IsJar(e))
                    .Select(e => new PlannedFile { FileName = e.Name, ZipEntry = e.FullName })
                    .ToList();

                var manifestEntries = ReadFileEntries(root);
                var labels = ReadModListLabels(archive, prefix, manifestEntries.Count);
                var pending = new List<PendingSpec>();

                var resolved = curseForge.IsConfigured
                    ? await curseForge.ResolveAsync(manifestEntries.Select(e => e.FileId).ToList(), cancellationToken)
                    : new Dictionary<int, CurseForgeFile>();

                for (var i = 0; i < manifestEntries.Count; i++)
                {
                    var (projectId, fileId) = manifestEntries[i];
                    var label = labels is null ? null : labels[i];

                    if (!resolved.TryGetValue(fileId, out var file))
                    {
                        pending.Add(new PendingSpec
                        {
                            Reason = curseForge.IsConfigured ? PendingReason.DownloadFailed : PendingReason.NoApiKey,
                            DisplayName = label,
                            ProjectId = projectId,
                            FileId = fileId,

                            SourceUrl = $"https://www.curseforge.com/projects/{projectId}",
                            Detail = curseForge.IsConfigured
                                ? "CurseForge did not return this file. Download the jar and supply it here."
                                : "No CurseForge:ApiKey is configured, so nothing about this file is knowable. Download the jar and supply it here.",
                        });
                        continue;
                    }

                    if (file.DownloadUrl is null)
                    {
                        var mirror = file.Sha1 is null
                            ? null
                            : await curseForge.FindOnModrinthBySha1Async(file.Sha1, cancellationToken);

                        if (mirror is not null && file.FileName is not null)
                        {
                            files.Add(new PlannedFile
                            {
                                FileName = file.FileName,
                                Downloads = [mirror],
                                Sha1 = file.Sha1,
                                Size = file.Length,
                            });
                            continue;
                        }

                        pending.Add(new PendingSpec
                        {
                            Reason = PendingReason.Blocked,
                            DisplayName = file.DisplayName ?? label,
                            FileName = file.FileName,
                            ProjectId = projectId,
                            FileId = fileId,

                            ExpectedSha1 = file.Sha1,
                            SourceUrl = $"https://www.curseforge.com/projects/{projectId}",
                            Detail = "The author disabled third-party distribution for this file. Download it from CurseForge and supply it here.",
                        });
                        continue;
                    }

                    files.Add(new PlannedFile
                    {
                        FileName = file.FileName ?? $"curseforge-{projectId}-{fileId}.jar",
                        Downloads = [file.DownloadUrl],
                        Sha1 = file.Sha1,
                        Size = file.Length,
                    });
                }

                return new PackPlan { Format = PackFormat.CurseForge, Files = files, Pending = pending };
            }
        }

        private static List<(int ProjectId, int FileId)> ReadFileEntries(JsonElement root)
        {
            var entries = new List<(int, int)>();

            if (!root.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
                return entries;

            foreach (var file in files.EnumerateArray())
            {
                if (file.TryGetProperty("projectID", out var project) && project.ValueKind == JsonValueKind.Number
                    && file.TryGetProperty("fileID", out var id) && id.ValueKind == JsonValueKind.Number)
                {
                    entries.Add((project.GetInt32(), id.GetInt32()));
                }
            }

            return entries;
        }

        private static List<string>? ReadModListLabels(ZipArchive archive, string prefix, int expected)
        {
            var entry = archive.GetEntry(prefix + "modlist.html");
            if (entry is null || expected == 0)
                return null;

            string html;
            using (var stream = entry.Open())
            using (var reader = new StreamReader(stream))
                html = reader.ReadToEnd();

            var labels = AnchorText().Matches(html)
                .Select(m => System.Net.WebUtility.HtmlDecode(m.Groups[1].Value).Trim())
                .ToList();

            return labels.Count == expected ? labels : null;
        }

        [GeneratedRegex("<a\\b[^>]*>(.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex AnchorText();
    }
}
