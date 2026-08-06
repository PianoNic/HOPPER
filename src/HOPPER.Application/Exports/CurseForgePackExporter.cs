using System.IO.Compression;
using System.Net;
using System.Text;
using HOPPER.Application.Exports.Schema;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Application.Exports
{
    /// <summary>Writes a CurseForge pack zip.
    ///
    /// The awkward format, and it is worth stating plainly rather than leaving as a surprise: a
    /// CurseForge files[] entry is TWO INTEGERS - a CurseForge project id and file id - and carries no
    /// filename, no URL, no hash and no size. HOPPER has neither integer for a Modrinth-sourced or a
    /// hand-uploaded mod and cannot invent them, so every such jar ships inline in overrides/mods/ and
    /// files[] comes out empty. That is a legitimate, importable pack; it is not a workaround, and it
    /// is the same reason PendingMod exists on the import side.
    ///
    /// The branch that will one day populate files[] is written and guarded below. Nothing produces
    /// CurseForge provenance yet, but when the pack importer starts recording it, those mods move out
    /// of overrides and into the manifest with no change here.</summary>
    public class CurseForgePackExporter(HopperDbContext db, IBlobStorage blobs, IConfiguration configuration)
        : PackExporterBase(db, blobs, configuration), IPackExporter
    {
        public PackFormat Format => PackFormat.CurseForge;

        public async Task<PackExportResult> ExportAsync(Guid serverId, CancellationToken cancellationToken)
        {
            var context = await LoadAsync(serverId, cancellationToken);
            var warnings = new List<string>();

            var referenced = new List<(Mod Mod, CurseForgeFileEntry Entry)>();
            var bundled = new List<Mod>();

            foreach (var mod in context.Mods)
            {
                if (CurseForgeEntry(mod) is { } entry)
                    referenced.Add((mod, entry));
                else
                    bundled.Add(mod);
            }

            var scratch = CreateScratchFile();
            try
            {
                using (var archive = new ZipArchive(scratch, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteJsonEntry(archive, "manifest.json", BuildManifest(context, referenced.Select(r => r.Entry).ToList()));
                    WriteTextEntry(archive, "modlist.html", BuildModList(context.Mods));

                    // Only what is NOT in files[]. A jar listed in the manifest and also present in
                    // overrides would be downloaded and then overwritten by itself.
                    foreach (var mod in bundled)
                        WriteBlobEntry(archive, "overrides/mods", mod, warnings);
                }

                return Finish(scratch, FileNameFor(context, "zip"), "application/zip", warnings);
            }
            catch
            {
                await scratch.DisposeAsync();
                throw;
            }
        }

        /// <summary>The forward-looking branch. CurseForge ids are numeric, which is exactly why they
        /// cannot be filled from Modrinth provenance: those ids are base62 strings and int.TryParse
        /// refuses them, so a Modrinth mod can never take this path by accident.</summary>
        private static CurseForgeFileEntry? CurseForgeEntry(Mod mod)
        {
            if (mod.Source != ModSource.CurseForge)
                return null;

            if (!int.TryParse(mod.ProjectId, out var projectId) || !int.TryParse(mod.VersionId, out var fileId))
                return null;

            return new CurseForgeFileEntry { ProjectId = projectId, FileId = fileId, Required = true };
        }

        private static CurseForgeManifest BuildManifest(ExportContext context, IReadOnlyList<CurseForgeFileEntry> files) => new()
        {
            Minecraft = new CurseForgeMinecraft
            {
                Version = context.MinecraftVersion,
                ModLoaders =
                [
                    new CurseForgeModLoader
                    {
                        // Bare loader build with no Minecraft prefix - "forge-47.4.10". Consumers strip
                        // the prefix and take the rest as the loader version verbatim.
                        Id = $"{LoaderIds.CurseForgePrefix(context.Loader)}-{context.LoaderVersion}",
                        Primary = true,
                    },
                ],
            },
            ManifestType = "minecraftModpack",
            ManifestVersion = 1,
            Name = context.Server.Name,
            Version = PackVersion(context.UtcNow),
            Author = "HOPPER",
            Overrides = "overrides",
            Files = files,
        };

        /// <summary>A flat list of what is in the pack. Optional in the format and purely for a human
        /// reading the zip, which is why a missing project name degrades to the filename rather than
        /// being omitted.</summary>
        private static string BuildModList(IReadOnlyList<Mod> mods)
        {
            var html = new StringBuilder();
            html.Append("<ul>\n");

            foreach (var mod in mods)
            {
                var label = WebUtility.HtmlEncode(mod.ProjectName ?? mod.FileName);

                // Linked only where a slug-shaped project id is actually recorded. Everything here is
                // HTML-encoded: a project title is upstream text and this file is opened in a browser.
                if (mod.Source == ModSource.Modrinth && !string.IsNullOrWhiteSpace(mod.ProjectId))
                {
                    var url = WebUtility.HtmlEncode($"https://modrinth.com/mod/{mod.ProjectId}");
                    html.Append($"  <li><a href=\"{url}\">{label}</a></li>\n");
                }
                else
                {
                    html.Append($"  <li>{label}</li>\n");
                }
            }

            html.Append("</ul>\n");
            return html.ToString();
        }
    }
}
