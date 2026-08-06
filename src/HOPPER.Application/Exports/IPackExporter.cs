using HOPPER.Domain.Enums;

namespace HOPPER.Application.Exports
{
    /// <summary>A finished pack, as a stream the controller hands straight to the client.
    ///
    /// A Stream and not a byte[]: a server of content mods exports to hundreds of megabytes and
    /// materialising that on the managed heap is how a small deployment falls over. The stream owns
    /// its temp file and deletes it on close, so the caller only has to dispose it - which
    /// File(stream, ...) does.</summary>
    public sealed record PackExportResult(
        string FileName,
        string ContentType,
        Stream Content,
        IReadOnlyList<string> Warnings);

    /// <summary>One implementation per format, selected by <see cref="Format"/>.</summary>
    public interface IPackExporter
    {
        PackFormat Format { get; }

        Task<PackExportResult> ExportAsync(Guid serverId, CancellationToken cancellationToken);
    }
}
