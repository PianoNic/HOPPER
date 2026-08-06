using System.IO.Compression;
using HOPPER.Application.Exports.Schema;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Application.Exports
{
    /// <summary>Writes a Modrinth .mrpack.
    ///
    /// This is the mirror image of ModrinthPlanner, which reads the same file, and the split it makes
    /// is the reason provenance exists at all: a mod HOPPER knows the Modrinth origin of becomes a
    /// files[] entry pointing at the real CDN URL with the hashes Modrinth published, and everything
    /// else - hand-uploaded, or imported from a pack before provenance was recorded - is written into
    /// overrides/mods/ as bytes.
    ///
    /// The test is HasModrinthProvenance() and NOT Source == Modrinth. A row that says Modrinth but is
    /// missing its download URL or a hash would otherwise produce a manifest entry with a null URL,
    /// which is an unusable pack; degrading it to an override is a correct pack either way.</summary>
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
                        WriteBlobEntry(archive, "overrides/mods", mod, warnings);
                }

                if (bundled.Count > 0)
                {
                    warnings.Add(
                        $"{bundled.Count} mods have no Modrinth origin recorded and ship inside the pack rather than as a download link.");
                }

                return Finish(scratch, FileNameFor(context, "mrpack"), "application/x-modrinth-modpack+zip", warnings);
            }
            catch
            {
                await scratch.DisposeAsync();
                throw;
            }
        }

        private static MrpackIndex BuildIndex(ExportContext context, IReadOnlyList<Mod> linked) => new()
        {
            FormatVersion = 1,
            Game = "minecraft",
            VersionId = PackVersion(context.UtcNow),
            Name = context.Server.Name,
            Summary = "Exported from HOPPER",

            // minecraft plus exactly one loader key. Nothing else is ever emitted: an unrecognised key
            // is a hard failure in the consumers, and the format's authors reserve the right to add
            // ids, which is a reason to be liberal on import and strict here.
            Dependencies = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["minecraft"] = context.MinecraftVersion,
                [LoaderIds.MrpackKey(context.Loader)] = context.LoaderVersion,
            },

            Files = linked.Select(m => new MrpackFile
            {
                // Always flat. Real packs contain deeper paths and the importer handles them, but
                // there is no reason to produce one.
                Path = $"mods/{ModFileNameValidator.Validate(m.FileName)}",

                // The hashes Modrinth published, verbatim, and nothing else. HOPPER's sha256 is not an
                // algorithm this format knows - it is the blob address and it stays there.
                Hashes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["sha1"] = m.Sha1!,
                    ["sha512"] = m.Sha512!,
                },

                Env = new MrpackEnv(),
                Downloads = [m.DownloadUrl!],
                FileSize = m.Size,
            }).ToList(),
        };
    }
}
