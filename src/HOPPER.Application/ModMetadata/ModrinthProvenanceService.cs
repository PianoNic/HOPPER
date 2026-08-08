using HOPPER.Application.Modrinth;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HOPPER.Application.ModMetadata
{
    /// Works out what a hand-uploaded jar actually is by asking Modrinth about its hash, the way
    /// Prism's EnsureMetadataTask does. A jar that turns out to be a Modrinth release then behaves
    /// like one: the browser shows it as installed, and it has a URL to re-download from.
    public sealed class ModrinthProvenanceService(
        IServiceScopeFactory scopes,
        ILogger<ModrinthProvenanceService> log) : BackgroundService
    {
        /// Modrinth's own cap on a bulk hash lookup.
        private const int BatchSize = 100;

        /// Prism asks again every run because a person starts it. This one runs unattended, so a jar
        /// Modrinth has never heard of is left alone until it is worth another look.
        private static readonly TimeSpan AskAgainAfter = TimeSpan.FromDays(30);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                var identified = 0;
                var asked = 0;

                var cutoff = DateTime.UtcNow - AskAgainAfter;

                List<Guid> pending;

                await using (var scope = scopes.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();

                    pending = await db.Mods.AsNoTracking()
                        .Where(m => m.ProjectId == null
                                    && m.Sha512 != null
                                    && (m.ProvenanceCheckedAt == null || m.ProvenanceCheckedAt < cutoff))
                        .OrderBy(m => m.Id)
                        .Select(m => m.Id)
                        .ToListAsync(stoppingToken);
                }

                for (var offset = 0; offset < pending.Count && !stoppingToken.IsCancellationRequested; offset += BatchSize)
                {
                    var batch = pending.GetRange(offset, Math.Min(BatchSize, pending.Count - offset));

                    await using var scope = scopes.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
                    var modrinth = scope.ServiceProvider.GetRequiredService<IModrinthClient>();

                    var rows = await db.Mods.Where(m => batch.Contains(m.Id)).ToListAsync(stoppingToken);

                    var found = await modrinth.GetVersionsByHashAsync(
                        rows.Select(r => r.Sha512!).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                        stoppingToken);

                    asked += rows.Count;

                    foreach (var row in rows)
                    {
                        row.ProvenanceCheckedAt = DateTime.UtcNow;

                        if (!found.TryGetValue(row.Sha512!, out var version))
                            continue;

                        Adopt(row, version);
                        identified++;
                    }

                    await db.SaveChangesAsync(stoppingToken);
                }

                if (asked > 0)
                    log.LogInformation("Asked Modrinth about {Asked} jar(s); {Identified} were releases it publishes.", asked, identified);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Modrinth provenance lookup did not complete. It will be retried on the next start.");
            }
        }

        /// The same bytes under the same hash, so this is not a guess about what the jar is - it is
        /// the release Modrinth publishes. The filename stays as uploaded.
        private static void Adopt(Domain.Mod row, ModrinthVersion version)
        {
            var file = version.PrimaryFile();

            row.Source = ModSource.Modrinth;
            row.ProjectId = version.ProjectId;
            row.VersionId = version.Id;
            row.DownloadUrl = file?.Url;
            row.Sha1 ??= file?.Sha1;
        }
    }
}
