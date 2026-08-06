using HOPPER.Application.Dtos.Modrinth;
using HOPPER.Application.Mappings.Modrinth;
using HOPPER.Application.Modrinth;
using HOPPER.Domain;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Modrinth
{
    /// <summary>The preview half of the two-phase add: what would actually be written, before
    /// anything is.
    ///
    /// A ticked optional arrives in <see cref="OptionalVersionIds"/> and is resolved as a ROOT, not as
    /// an optional. That is deliberate and it is the whole mechanism behind "nothing arrives unseen":
    /// ticking one re-runs this query, so whatever that optional itself requires appears in the "will
    /// be added" list before the admin can confirm.
    ///
    /// It is a query and it writes nothing, but the controller exposes it as a POST - two arrays of
    /// version ids do not fit a query string once optionals are being ticked on and off.</summary>
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

        /// <summary>What the server carries, read once and handed to the resolver as data. The
        /// resolver never touches the database itself, which is what keeps it a pure function of its
        /// inputs and drivable from fixtures.</summary>
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
