using HOPPER.Application.Dtos.Imports;
using HOPPER.Application.Mappings.Imports;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Imports
{
    public record ListImportsQuery(Guid ServerId) : IQuery<IReadOnlyList<ModImportDto>>;

    public class ListImportsQueryHandler(HopperDbContext db)
        : IQueryHandler<ListImportsQuery, IReadOnlyList<ModImportDto>>
    {
        private const int MaxRows = 25;

        public async ValueTask<IReadOnlyList<ModImportDto>> Handle(ListImportsQuery query, CancellationToken cancellationToken)
        {
            var rows = await db.ModImports.AsNoTracking()
                .Where(i => i.ServerId == query.ServerId)
                .OrderByDescending(i => i.CreatedAt)
                .Take(MaxRows)
                .ToListAsync(cancellationToken);

            return rows.Select(i => i.ToDto()).ToList();
        }
    }
}
