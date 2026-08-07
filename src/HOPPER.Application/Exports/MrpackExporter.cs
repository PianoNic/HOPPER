using System.IO.Compression;
using HOPPER.Application.Exports.Schema;
using HOPPER.Application.Imports;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Application.Exports
{
    public class MrpackExporter(HopperDbContext db, IBlobStorage blobs, IConfiguration configuration)
        : PackExporterBase(db, blobs, configuration), IPackExporter
    {
        public PackFormat Format => PackFormat.Modrinth;

        public async Task<PackExportResult> ExportAsync(Guid serverId, CancellationToken cancellationToken)
        {
            var context = await LoadAsync(serverId, cancellationToken);
            var warnings = new List<string>();

            var linked = context.Mods.Where(m => m.HasModrinthProvenance()).ToList();
            var bundled = context.Mods.Where(m => !m.HasModrinthProvenance()).ToList();

            var scratch = CreateScratchFile();
            try
            {
                using (var archive = new ZipArchive(scratch, ZipArchiveMode.Create, leaveOpen: true))
                {
                    WriteJsonEntry(archive, "modrinth.index.json", BuildIndex(context, linked));

                    foreach (var mod in bundled)
                        WriteBlobEntry(archive, OverrideFolder(mod.Side), mod, warnings);
                }

                return Finish(scratch, FileNameFor(context, "mrpack"), "application/x-modrinth-modpack+zip", warnings);
            }
            catch
            {
                await scratch.DisposeAsync();
                throw;
            }
        }

        // The folder names are the declaration a pack carries for a bundled jar, and the same ones
        // ModrinthPlanner reads back. Writing everything to the shared folder is what threw the
        // classification away.
        private static string OverrideFolder(ModSide side) => side switch
        {
            ModSide.ClientOnly => "client-overrides/mods",
            ModSide.ServerOnly => "server-overrides/mods",
            _ => "overrides/mods",
        };

        private static MrpackEnv EnvFor(ModSide side)
        {
            var (client, server) = PackEnv.Wire(side);
            return new MrpackEnv { Client = client, Server = server };
        }

        private static MrpackIndex BuildIndex(ExportContext context, IReadOnlyList<Mod> linked) => new()
        {
            FormatVersion = 1,
            Game = "minecraft",
            VersionId = PackVersion(context.UtcNow),
            Name = context.Server.Name,
            Summary = "Exported from HOPPER",

            Dependencies = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["minecraft"] = context.MinecraftVersion,
                [LoaderIds.MrpackKey(context.Loader)] = context.LoaderVersion,
            },

            Files = linked.Select(m => new MrpackFile
            {
                Path = $"mods/{ModFileNameValidator.Validate(m.FileName)}",

                Hashes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sha1"] = m.Sha1!,
                    ["sha512"] = m.Sha512!,
                },

                Env = EnvFor(m.Side),
                Downloads = [m.DownloadUrl!],
                FileSize = m.Size,
            }).ToList(),
        };
    }
}
