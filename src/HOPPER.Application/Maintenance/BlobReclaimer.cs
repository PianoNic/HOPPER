using HOPPER.Application.Imports;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HOPPER.Application.Maintenance
{
    public sealed record ReclaimReport(int Blobs, int Scratch, int StagedPacks, int Imports);

    public sealed class BlobReclaimer(
        HopperDbContext db,
        IBlobStorage blobs,
        ImportStaging staging,
        IConfiguration configuration,
        ILogger<BlobReclaimer> log)
    {
        private const int BatchSize = 500;

        public static readonly TimeSpan DefaultGrace = TimeSpan.FromHours(1);

        public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

        public static readonly TimeSpan DefaultStallTimeout = TimeSpan.FromHours(2);

        public static TimeSpan Grace(IConfiguration configuration) =>
            configuration.GetValue("Hopper:BlobReclaimGrace", DefaultGrace);

        public static TimeSpan Interval(IConfiguration configuration) =>
            configuration.GetValue("Hopper:BlobReclaimInterval", DefaultInterval);

        public static TimeSpan StallTimeout(IConfiguration configuration) =>
            configuration.GetValue("Hopper:ImportStallTimeout", DefaultStallTimeout);

        public async Task<ReclaimReport> SweepAsync(
            DateTime utcNow, bool afterRestart = false, CancellationToken cancellationToken = default)
        {
            var cutoff = utcNow - Grace(configuration);

            var imports = await ReconcileImportsAsync(utcNow, afterRestart, cancellationToken);
            var stagedPacks = await SweepStagingAsync(cutoff, cancellationToken);
            var scratch = SweepScratch(cutoff);
            var unreferenced = await CanTrustTheDatabaseAsync(cancellationToken)
                ? await SweepBlobsAsync(cutoff, cancellationToken)
                : 0;

            if (unreferenced + scratch + stagedPacks > 0)
            {
                log.LogInformation(
                    "Reclaimed {Blobs} unreferenced blob(s), {Scratch} scratch file(s) and {Packs} staged pack(s).",
                    unreferenced, scratch, stagedPacks);
            }

            return new ReclaimReport(unreferenced, scratch, stagedPacks, imports);
        }

        private async Task<int> ReconcileImportsAsync(DateTime utcNow, bool afterRestart, CancellationToken cancellationToken)
        {
            var stallTimeout = StallTimeout(configuration);
            var stallCutoff = utcNow - stallTimeout;

            var ids = await db.ModImports.AsNoTracking()
                .Where(i => (i.Status == ImportStatus.Queued || i.Status == ImportStatus.Running)
                            && (afterRestart || i.UpdatedAt < stallCutoff))
                .Select(i => i.Id)
                .ToListAsync(cancellationToken);

            if (ids.Count == 0)
                return 0;

            var reason = afterRestart
                ? "The import did not survive a restart. Start it again."
                : $"The import stopped responding and was ended after {stallTimeout}.";

            await db.ModImports
                .Where(i => ids.Contains(i.Id))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(i => i.Status, ImportStatus.Failed)
                    .SetProperty(i => i.Error, reason)
                    .SetProperty(i => i.CompletedAt, utcNow)
                    .SetProperty(i => i.UpdatedAt, utcNow), cancellationToken);

            foreach (var id in ids)
                staging.Cleanup(id);

            log.LogWarning("Ended {Count} import(s) that were still marked queued or running. {Reason}", ids.Count, reason);

            return ids.Count;
        }

        private async Task<int> SweepStagingAsync(DateTime cutoff, CancellationToken cancellationToken)
        {
            var root = BlobPaths.Imports(configuration);
            if (!Directory.Exists(root))
                return 0;

            var candidates = new List<(string Path, bool IsDirectory, Guid? ImportId)>();

            foreach (var file in Directory.EnumerateFiles(root, "*.pack"))
            {
                if (File.GetLastWriteTimeUtc(file) >= cutoff)
                    continue;

                candidates.Add((file, false, ParseId(Path.GetFileNameWithoutExtension(file))));
            }

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                if (Directory.GetLastWriteTimeUtc(directory) >= cutoff)
                    continue;

                candidates.Add((directory, true, ParseId(Path.GetFileName(directory))));
            }

            if (candidates.Count == 0)
                return 0;

            var ids = candidates.Where(c => c.ImportId is not null).Select(c => c.ImportId!.Value).Distinct().ToList();

            var live = await db.ModImports.AsNoTracking()
                .Where(i => ids.Contains(i.Id)
                            && (i.Status == ImportStatus.Queued || i.Status == ImportStatus.Running))
                .Select(i => i.Id)
                .ToListAsync(cancellationToken);

            var liveSet = live.ToHashSet();
            var removed = 0;

            foreach (var (path, isDirectory, importId) in candidates)
            {
                if (importId is not null && liveSet.Contains(importId.Value))
                    continue;

                if (TryDelete(path, isDirectory))
                    removed++;
            }

            return removed;
        }

        private int SweepScratch(DateTime cutoff)
        {
            var removed = 0;

            foreach (var file in blobs.EnumerateScratch().ToList())
            {
                if (file.LastWriteUtc >= cutoff)
                    continue;

                if (TryDelete(file.Path, isDirectory: false))
                    removed++;
            }

            return removed;
        }

        // A blob is deleted because no row claims it, which is a sound rule right up to the moment
        // the rows are not the ones that belong to this store. Pointed at a restored, fresh or simply
        // wrong database, every jar looks unreferenced and the sweep would take the lot - and the
        // blobs are the one thing HOPPER cannot rebuild from its own state.
        //
        // No servers at all is that signature. A real deployment with jars on disk has a server that
        // serves them; a genuinely fresh one has an empty store, so skipping costs it nothing.
        private async Task<bool> CanTrustTheDatabaseAsync(CancellationToken cancellationToken)
        {
            if (await db.Servers.AsNoTracking().AnyAsync(cancellationToken))
                return true;

            if (!blobs.EnumerateBlobs().Any())
                return true;

            log.LogWarning(
                "Skipped reclaiming blobs: this database has no servers but the blob store is not empty. "
                + "That is what a restored, fresh or misconfigured database looks like, and sweeping now "
                + "would delete every jar. Point HOPPER at the right database, or empty the store by hand.");

            return false;
        }

        private async Task<int> SweepBlobsAsync(DateTime cutoff, CancellationToken cancellationToken)
        {
            var candidates = blobs.EnumerateBlobs()
                .Where(b => b.LastWriteUtc < cutoff)
                .Select(b => b.Sha256)
                .ToList();

            var removed = 0;

            for (var offset = 0; offset < candidates.Count; offset += BatchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = candidates.GetRange(offset, Math.Min(BatchSize, candidates.Count - offset));

                var referencedJars = await db.Mods.AsNoTracking()
                    .Where(m => batch.Contains(m.Sha256))
                    .Select(m => m.Sha256)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                var referencedIcons = await db.Mods.AsNoTracking()
                    .Where(m => m.IconSha256 != null && batch.Contains(m.IconSha256))
                    .Select(m => m.IconSha256!)
                    .Distinct()
                    .ToListAsync(cancellationToken);

                var referenced = referencedJars.Concat(referencedIcons).ToHashSet(StringComparer.Ordinal);

                foreach (var sha in batch)
                {
                    if (referenced.Contains(sha))
                        continue;

                    if (await BlobCollector.CollectAsync(db, blobs, sha, cancellationToken))
                        removed++;
                }
            }

            return removed;
        }

        private static Guid? ParseId(string name) => Guid.TryParseExact(name, "N", out var id) ? id : null;

        private bool TryDelete(string path, bool isDirectory)
        {
            try
            {
                if (isDirectory)
                {
                    if (!Directory.Exists(path))
                        return false;

                    Directory.Delete(path, recursive: true);
                }
                else
                {
                    if (!File.Exists(path))
                        return false;

                    File.Delete(path);
                }

                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.LogDebug(ex, "Could not reclaim {Path}. It will be retried on the next sweep.", path);
                return false;
            }
        }
    }
}
