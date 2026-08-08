using HOPPER.Application.Modrinth;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HOPPER.Application.ModMetadata
{
    /// Works out what a hand-uploaded jar is by its hash, the way Prism's EnsureMetadataTask does.
    public sealed class ModrinthProvenanceService(
        IServiceScopeFactory scopes,
        ILogger<ModrinthProvenanceService> log) : BackgroundService
    {
        /// Modrinth's own cap on a bulk hash lookup.
        private const int BatchSize = 100;

        /// Prism re-asks every run because a person starts it; this runs unattended.
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

                    // Name and icon live on the project, not the version. Matches only.
                    var projects = await ProjectsAsync(modrinth, found.Values, stoppingToken);

                    foreach (var row in rows)
                    {
                        row.ProvenanceCheckedAt = DateTime.UtcNow;

                        if (!found.TryGetValue(row.Sha512!, out var version))
                            continue;

                        Adopt(row, version, projects.GetValueOrDefault(version.ProjectId ?? string.Empty));
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

        private static async Task<Dictionary<string, ModrinthProject>> ProjectsAsync(
            IModrinthClient modrinth, IEnumerable<ModrinthVersion> versions, CancellationToken cancellationToken)
        {
            var ids = versions
                .Select(v => v.ProjectId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList()!;

            if (ids.Count == 0)
                return new Dictionary<string, ModrinthProject>(StringComparer.Ordinal);

            var projects = await modrinth.GetProjectsAsync(ids!, cancellationToken);

            return projects
                .Where(p => !string.IsNullOrWhiteSpace(p.Id))
                .ToDictionary(p => p.Id!, p => p, StringComparer.Ordinal);
        }

        /// Same bytes under the same hash, so this is identification, not a guess.
        private static void Adopt(Domain.Mod row, ModrinthVersion version, ModrinthProject? project)
        {
            var file = version.PrimaryFile();

            row.Source = ModSource.Modrinth;
            row.ProjectId = version.ProjectId;
            row.VersionId = version.Id;
            row.DownloadUrl = file?.Url;
            row.Sha1 ??= file?.Sha1;

            row.ProjectName = project?.Title;

            // Fallback; the table prefers the icon read out of the jar.
            row.IconUrl = project?.IconUrl;
        }
    }
}
