using HOPPER.Application.Exports;
using HOPPER.Domain.Enums;
using Mediator;

namespace HOPPER.Application.Queries.Exports
{
    public record ExportServerPackQuery(Guid ServerId, PackFormat Format) : IQuery<PackExportResult>;

    public class ExportServerPackQueryHandler(IEnumerable<IPackExporter> exporters)
        : IQueryHandler<ExportServerPackQuery, PackExportResult>
    {
        public async ValueTask<PackExportResult> Handle(ExportServerPackQuery query, CancellationToken cancellationToken)
        {
            var exporter = exporters.FirstOrDefault(e => e.Format == query.Format)
                ?? throw new ArgumentException(
                    $"{query.Format} is not a pack HOPPER can write. Choose Modrinth, CurseForge or a Prism instance.");

            return await exporter.ExportAsync(query.ServerId, cancellationToken);
        }
    }
}
