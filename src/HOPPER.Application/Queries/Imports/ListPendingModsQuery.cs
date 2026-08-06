using HOPPER.Application.Dtos.Imports;
using HOPPER.Application.Mappings.Imports;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Imports
{
    public record ListPendingModsQuery(Guid ServerId) : IQuery<IReadOnlyList<PendingModDto>>;

    public class ListPendingModsQueryHandler(HopperDbContext db)
        : IQueryHandler<ListPendingModsQuery, IReadOnlyList<PendingModDto>>
    {
        public async ValueTask<IReadOnlyList<PendingModDto>> Handle(ListPendingModsQuery query, CancellationToken cancellationToken)
        {
            var rows = await db.PendingMods.AsNoTracking()
                .Where(p => p.ServerId == query.ServerId)
                .OrderBy(p => p.CreatedAt)
                .ToListAsync(cancellationToken);

            return rows.Select(p => p.ToDto()).ToList();
        }
    }
}
