using HOPPER.Application.Dtos.Imports;
using HOPPER.Application.Mappings.Imports;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Imports
{
    public record GetImportQuery(Guid ServerId, Guid ImportId) : IQuery<ModImportDto>;

    public class GetImportQueryHandler(HopperDbContext db) : IQueryHandler<GetImportQuery, ModImportDto>
    {
        public async ValueTask<ModImportDto> Handle(GetImportQuery query, CancellationToken cancellationToken)
        {
            var import = await db.ModImports.AsNoTracking()
                .FirstOrDefaultAsync(i => i.ServerId == query.ServerId && i.Id == query.ImportId, cancellationToken)
                ?? throw new ImportNotFoundException(query.ImportId);

            return import.ToDto();
        }
    }

    public sealed class ImportNotFoundException(Guid id)
        : InvalidOperationException($"No import with id {id} on this server.")
    {
        public Guid ImportId { get; } = id;
    }
}
