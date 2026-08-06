using HOPPER.Application.Dtos.Modrinth;
using HOPPER.Application.Mappings.Modrinth;
using HOPPER.Application.Modrinth;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Modrinth
{
    public record SearchModrinthQuery(
        Guid? ServerId,
        string? Query,
        string? Loader,
        string? GameVersion,
        ModrinthSearchIndex Index,
        int Offset,
        int Limit) : IQuery<ModrinthSearchResultDto>;

    public class SearchModrinthQueryHandler(IModrinthClient modrinth, HopperDbContext db)
        : IQueryHandler<SearchModrinthQuery, ModrinthSearchResultDto>
    {
        public async ValueTask<ModrinthSearchResultDto> Handle(SearchModrinthQuery query, CancellationToken cancellationToken)
        {
            var response = await modrinth.SearchAsync(
                query.Query, query.Loader, query.GameVersion, query.Index, query.Offset, query.Limit, cancellationToken);

            var installed = await InstalledProjectIdsAsync(query.ServerId, cancellationToken);

            return new ModrinthSearchResultDto
            {
                Hits = response.Hits.Select(h => h.ToDto(installed.Contains(h.ProjectId))).ToList(),
                Offset = response.Offset,
                Limit = response.Limit,
                TotalHits = response.TotalHits,
            };
        }

        private async Task<HashSet<string>> InstalledProjectIdsAsync(Guid? serverId, CancellationToken cancellationToken)
        {
            if (serverId is not { } id)
                return [];

            var ids = await db.Mods.AsNoTracking()
                .Where(m => m.ServerId == id && m.ProjectId != null)
                .Select(m => m.ProjectId!)
                .Distinct()
                .ToListAsync(cancellationToken);

            return ids.ToHashSet(StringComparer.Ordinal);
        }
    }
}
