using HOPPER.Domain.Enums;

namespace HOPPER.Application.Exports
{
    public sealed record PackExportResult(
        string FileName,
        string ContentType,
        Stream Content,
        IReadOnlyList<string> Warnings);

    public interface IPackExporter
    {
        PackFormat Format { get; }

        Task<PackExportResult> ExportAsync(Guid serverId, CancellationToken cancellationToken);
    }
}
