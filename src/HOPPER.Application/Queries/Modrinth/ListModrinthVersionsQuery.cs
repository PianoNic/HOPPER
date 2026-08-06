using HOPPER.Application.Dtos.Modrinth;
using HOPPER.Application.Mappings.Modrinth;
using HOPPER.Application.Modrinth;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Modrinth
{
    public record ListModrinthVersionsQuery(
        Guid? ServerId,
        string IdOrSlug,
        string? Loader,
        string? GameVersion) : IQuery<IReadOnlyList<ModrinthVersionDto>>;

    public class ListModrinthVersionsQueryHandler(IModrinthClient modrinth, HopperDbContext db)
        : IQueryHandler<ListModrinthVersionsQuery, IReadOnlyList<ModrinthVersionDto>>
    {
        public async ValueTask<IReadOnlyList<ModrinthVersionDto>> Handle(
            ListModrinthVersionsQuery query, CancellationToken cancellationToken)
        {
            var versions = await modrinth.ListVersionsAsync(
                query.IdOrSlug, query.Loader, query.GameVersion, includeChangelog: false, cancellationToken);

            var installed = await InstalledVersionIdsAsync(query.ServerId, cancellationToken);

            return versions.Select(v => v.ToDto(installed.Contains(v.Id))).ToList();
        }

        private async Task<HashSet<string>> InstalledVersionIdsAsync(Guid? serverId, CancellationToken cancellationToken)
        {
            if (serverId is not { } id)
                return [];

            var ids = await db.Mods.AsNoTracking()
                .Where(m => m.ServerId == id && m.VersionId != null)
                .Select(m => m.VersionId!)
                .ToListAsync(cancellationToken);

            return ids.ToHashSet(StringComparer.Ordinal);
        }
    }
}
