using HOPPER.Application.Dtos.Modrinth;
using HOPPER.Application.Mappings.Modrinth;
using HOPPER.Application.Modrinth;
using HOPPER.Domain;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Modrinth
{
    public record PlanModrinthInstallQuery(
        Guid ServerId,
        IReadOnlyList<string> RootVersionIds,
        IReadOnlyList<string> OptionalVersionIds) : IQuery<ModrinthInstallPlanDto>;

    public class PlanModrinthInstallQueryHandler(HopperDbContext db, IModrinthDependencyResolver resolver)
        : IQueryHandler<PlanModrinthInstallQuery, ModrinthInstallPlanDto>
    {
        public async ValueTask<ModrinthInstallPlanDto> Handle(
            PlanModrinthInstallQuery query, CancellationToken cancellationToken)
        {
            var server = await db.Servers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == query.ServerId, cancellationToken)
                ?? throw new ServerNotFoundException(query.ServerId);

            var (gameVersion, loader) = ServerPlatform.RequireForBrowsing(server);

            var installed = await InstalledAsync(query.ServerId, cancellationToken);

            var roots = query.RootVersionIds
                .Concat(query.OptionalVersionIds)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var result = await resolver.ResolveAsync(
                new ResolveRequest
                {
                    RootVersionIds = roots,
                    Loader = loader,
                    GameVersion = gameVersion,
                    Installed = installed,
                },
                cancellationToken);

            return result.ToDto();
        }

        private async Task<IReadOnlyList<InstalledMod>> InstalledAsync(Guid serverId, CancellationToken cancellationToken)
        {
            var rows = await db.Mods.AsNoTracking()
                .Where(m => m.ServerId == serverId)
                .Select(m => new { m.ProjectId, m.VersionId, m.FileName })
                .ToListAsync(cancellationToken);

            return rows.Select(r => new InstalledMod(r.ProjectId, r.VersionId, r.FileName)).ToList();
        }
    }
}
