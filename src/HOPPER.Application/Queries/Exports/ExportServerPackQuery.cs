using HOPPER.Application.Exports;
using HOPPER.Domain.Enums;
using Mediator;

namespace HOPPER.Application.Queries.Exports
{
    /// <summary>Exports one server as a downloadable pack.
    ///
    /// PackFormat is reused rather than a fourth enum being introduced: Modrinth, CurseForge and
    /// PrismInstance mean exactly the same three things reading a pack as they do writing one, and the
    /// dashboard already has a label table for them. Unknown and JarArchive are not export formats -
    /// one is a detection failure and the other is a bare bag of jars with no manifest at all - so
    /// both are refused with a 400 rather than silently defaulting to something.</summary>
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
