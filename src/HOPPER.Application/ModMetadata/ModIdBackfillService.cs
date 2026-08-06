using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HOPPER.Application.ModMetadata
{
    public sealed class ModIdBackfillService(IServiceScopeFactory scopes, ILogger<ModIdBackfillService> log)
        : BackgroundService
    {
        private const int BatchSize = 200;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                var filled = 0;

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
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Mod id backfill did not complete. It will be retried on the next start.");
            }
        }
    }
}
