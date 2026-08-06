using HOPPER.Application.Dtos.Modrinth;
using HOPPER.Application.Mappings.Modrinth;
using HOPPER.Application.Modrinth;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Modrinth
{
    /// <summary>The version picker's list, newest first.
    ///
    /// Loader and game version go to the API as the separate loaders= and game_versions= parameters,
    /// NOT as search facets - two endpoints, two vocabularies. Both are JSON-array encoded inside the
    /// client, because a bare string there is silently ignored and the whole list comes back
    /// unfiltered rather than as an error.</summary>
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
            // include_changelog: false always. The list is 35% smaller without them on a narrow query
            // and the changelog is only ever read for the one version being inspected.
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
