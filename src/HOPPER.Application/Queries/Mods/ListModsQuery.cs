using HOPPER.Application.Dtos.Mods;
using HOPPER.Application.Mappings.Mods;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Mods
{
    public record ListModsQuery : IQuery<IReadOnlyList<ModDto>>;

    public class ListModsQueryHandler(HopperDbContext db) : IQueryHandler<ListModsQuery, IReadOnlyList<ModDto>>
    {
        public async ValueTask<IReadOnlyList<ModDto>> Handle(ListModsQuery query, CancellationToken cancellationToken)
        {
            // Same order as the manifest, so the admin list and what the clients actually receive
            // read as the same list.
            var rows = await db.Mods.AsNoTracking().OrderBy(m => m.FileName).ToListAsync(cancellationToken);
            return rows.Select(m => m.ToDto()).ToList();
        }
    }
}
