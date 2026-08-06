using System.IO.Compression;
using System.Text;
using HOPPER.Application.Exports.Schema;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Application.Exports
{
    /// <summary>Writes a Prism / MultiMC instance zip: instance.cfg, mmc-pack.json and a materialised
    /// minecraft/mods directory.
    ///
    /// Two rules here are not stylistic and both come from how Prism actually reads a zip.
    ///
    /// One: this archive must NOT contain modrinth.index.json. Prism's detection order puts
    /// modrinth.index.json above instance.cfg, so an instance zip carrying one is imported as a
    /// Modrinth pack and the instance.cfg is ignored outright. "A Prism instance wrapping an mrpack"
    /// is not a thing that exists - the .mrpack already is that, and Prism builds the instance from it
    /// itself.
    ///
    /// Two: the game directory is "minecraft/", not ".minecraft/". That is what Prism creates on
    /// Windows, it is what PrismPlanner prefers when reading one back, and it is therefore what makes
    /// HOPPER's own export re-importable into HOPPER. Both spellings are accepted by Prism; only one
    /// round-trips here.</summary>
    public class PrismInstanceExporter(HopperDbContext db, IBlobStorage blobs, IConfiguration configuration)
        : PackExporterBase(db, blobs, configuration), IPackExporter
    {
        public PackFormat Format => PackFormat.PrismInstance;

        public async Task<PackExportResult> ExportAsync(Guid serverId, CancellationToken cancellationToken)
        {
            var context = await LoadAsync(serverId, cancellationToken);
            var warnings = new List<string>();

            var scratch = CreateScratchFile();
            try
            {
                using (var archive = new ZipArchive(scratch, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteTextEntry(archive, "instance.cfg", BuildInstanceCfg(context));
                    WriteJsonEntry(archive, "mmc-pack.json", BuildPack(context));

                    // An instance is a materialised game directory, so every mod goes in as bytes -
                    // there is no manifest here to carry a download link for the Modrinth ones.
                    foreach (var mod in context.Mods)
                        WriteBlobEntry(archive, "minecraft/mods", mod, warnings);
                }

                return Finish(scratch, FileNameFor(context, "zip"), "application/zip", warnings);
            }
            catch
            {
                await scratch.DisposeAsync();
                throw;
            }
        }

        /// <summary>InstanceType is the only load-bearing key: Prism rejects an instance outright if it
        /// is present and not "OneSix". The name is overwritten by whatever the admin types in the
        /// import dialog and every Override*=false line and the whole [UI] block are defaults, so none
        /// of that is written.</summary>
        private static string BuildInstanceCfg(ExportContext context)
        {
            var cfg = new StringBuilder();
            cfg.Append("[General]\n");
            cfg.Append("ConfigVersion=1.3\n");
            cfg.Append("InstanceType=OneSix\n");
            cfg.Append("iconKey=default\n");

            // INI has no escaping worth relying on, so a newline in a server name would end the value
            // and make the next line look like a key.
            cfg.Append($"name={OneLine(context.Server.Name)}\n");
            cfg.Append("notes=Exported from HOPPER\n");
            return cfg.ToString();
        }

        private static MmcPack BuildPack(ExportContext context) => new()
        {
            FormatVersion = 1,
            Components =
            [
                new MmcComponent
                {
                    Uid = LoaderIds.MinecraftUid,
                    Version = context.MinecraftVersion,
                    Important = true,
                },
                new MmcComponent
                {
                    Uid = LoaderIds.PrismUid(context.Loader),
                    Version = context.LoaderVersion,
                },
            ],
        };

        private static string OneLine(string value) =>
            value.Replace("\r", " ").Replace("\n", " ").Trim();
    }
}
