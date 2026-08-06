using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using HOPPER.Domain.Enums;

namespace HOPPER.Application.Imports
{
    /// <summary>Plans a CurseForge pack zip. This is the hard one, and the reason PendingMod exists at
    /// all: a files[] entry is two integers and nothing else - no filename, no URL, no hash, no size -
    /// so everything needed to fetch or even name a mod lives behind an API key HOPPER does not ship.
    ///
    /// Without a key: the overrides/ jars are imported and every manifest entry becomes pending.
    /// With a key: entries resolve to real URLs, and only the ones whose authors disabled
    /// distribution stay pending. That is exactly what Prism's BlockedModsDialog exists for.</summary>
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

                // "overrides" names the folder; it is not fixed and must be read rather than assumed,
                // even though every pack in the wild says "overrides".
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
                            // Redirects to the project page. The slug is not in the manifest, so this
                            // is the best link that can be produced offline.
                            SourceUrl = $"https://www.curseforge.com/projects/{projectId}",
                            Detail = curseForge.IsConfigured
                                ? "CurseForge did not return this file. Download the jar and supply it here."
                                : "No CurseForge:ApiKey is configured, so nothing about this file is knowable. Download the jar and supply it here.",
                        });
                        continue;
                    }

                    if (file.DownloadUrl is null)
                    {
                        // The genuine blocked case. Prism tests exactly this - an empty downloadUrl -
                        // and tries Modrinth by the sha1 CurseForge did give us before giving up.
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
                            // Populated here and not in the keyless branch, which is what makes a
                            // supplied jar verifiable rather than merely asserted.
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

        /// <summary>modlist.html is a flat &lt;ul&gt; of project links with no ids in it, so it cannot
        /// be JOINED to files[] - only lined up positionally, and only when the counts agree exactly.
        /// Returns null otherwise. These are labels for a human to recognise a mod by, never a key.</summary>
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
