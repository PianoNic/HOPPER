using System.IO.Compression;
using System.Text;
using HOPPER.Application.Exports.Schema;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Application.Exports
{
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

                    var withheld = context.Mods.Count(m => !ModSideRules.Reaches(m.Side, SyncSide.Client));
                    if (withheld > 0)
                    {
                        warnings.Add($"Left out {withheld} server-only jar{(withheld == 1 ? "" : "s")}. A Prism instance is a client game directory, so a jar marked Server only would be loaded there.");
                    }

                    foreach (var mod in context.Mods.Where(m => ModSideRules.Reaches(m.Side, SyncSide.Client)))
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

        private static string BuildInstanceCfg(ExportContext context)
        {
            var cfg = new StringBuilder();
            cfg.Append("[General]\n");
            cfg.Append("ConfigVersion=1.3\n");
            cfg.Append("InstanceType=OneSix\n");
            cfg.Append("iconKey=default\n");

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
