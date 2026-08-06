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

        private static string BuildModList(IReadOnlyList<Mod> mods)
        {
            var html = new StringBuilder();
            html.Append("<ul>\n");

            foreach (var mod in mods)
            {
                var label = WebUtility.HtmlEncode(mod.ProjectName ?? mod.FileName);

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
