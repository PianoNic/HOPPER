using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HOPPER.Application.ModMetadata
{
    /// <summary>Fills Mod.ModIds in for rows that were stored before mod ids were extracted.
    ///
    /// Without this the feature does nothing on exactly the installs that need it. Every row on
    /// every already-deployed server carries null, and nothing ever re-uploads those jars: the blob
    /// is already on disk, the row is already correct in every other respect, and a client would
    /// keep colliding with the player's own copy forever.
    ///
    /// A BackgroundService rather than startup code on purpose. ExecuteAsync runs after the host has
    /// started, so it never delays boot, and it is guaranteed to run after the migrator - which has
    /// to have created the column before anything queries it.
    ///
    /// Idempotent by construction. Only null rows are selected, and a row whose blob has gone
    /// missing is LEFT null rather than written as empty: null means "we have not looked", empty
    /// means "we looked and there is nothing", and collapsing the two would turn a temporary
    /// storage problem into a permanent answer.</summary>
    public sealed class ModIdBackfillService(IServiceScopeFactory scopes, ILogger<ModIdBackfillService> log)
        : BackgroundService
    {
        /// <summary>One save per batch. Big enough that a thousand-mod install is a handful of round
        /// trips, small enough that the change tracker never holds a whole server's mod set.</summary>
        private const int BatchSize = 200;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                var filled = 0;

                // The work list is taken once, up front, as bare ids. A row whose blob is missing
                // stays null on purpose, so re-querying by nullness after every batch would hand
                // back the same unreadable rows forever; a fixed list cannot loop. Mod rows are
                // counted in hundreds per server, so holding the Guids costs nothing.
                List<Guid> pending;

                await using (var scope = scopes.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
                    pending = await db.Mods.AsNoTracking()
                        .Where(m => m.ModIds == null)
                        .OrderBy(m => m.Id)
                        .Select(m => m.Id)
                        .ToListAsync(stoppingToken);
                }

                for (var offset = 0; offset < pending.Count && !stoppingToken.IsCancellationRequested; offset += BatchSize)
                {
                    var batch = pending.GetRange(offset, Math.Min(BatchSize, pending.Count - offset));

                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
                    var blobs = scope.ServiceProvider.GetRequiredService<IBlobStorage>();

                    var rows = await db.Mods.Where(m => batch.Contains(m.Id)).ToListAsync(stoppingToken);

                    foreach (var row in rows)
                    {
                        // Re-checked because an upload may have written this row between the two
                        // queries. Never overwrite an id set that has already been read.
                        if (row.ModIds is not null)
                            continue;

                        var ids = ModIdReader.FromBlob(blobs, row.Sha256);
                        if (ids is null)
                            continue;

                        row.ModIds = ids;
                        filled++;
                    }

                    await db.SaveChangesAsync(stoppingToken);
                }

                if (filled > 0)
                    log.LogInformation("Backfilled mod ids for {Count} mod row(s).", filled);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Shutting down mid-pass. The rows that are still null are picked up next boot.
            }
            catch (Exception ex)
            {
                // A backfill that cannot run is a feature that stays dormant, not an API that fails
                // to start. Every other code path already checks ModIds for null.
                log.LogWarning(ex, "Mod id backfill did not complete. It will be retried on the next start.");
            }
        }
    }
}
