using HOPPER.Domain.Enums;

namespace HOPPER.Application.Imports
{
    public sealed record PlannedFile
    {
        public required string FileName { get; init; }

        public string? ZipEntry { get; init; }

        public IReadOnlyList<Uri> Downloads { get; init; } = [];

        public string? Sha512 { get; init; }

        public string? Sha1 { get; init; }

        public long? Size { get; init; }
    }

    public sealed record PendingSpec
    {
        public required PendingReason Reason { get; init; }
        public string? DisplayName { get; init; }
        public string? FileName { get; init; }
        public int? ProjectId { get; init; }
        public int? FileId { get; init; }
        public string? ExpectedSha1 { get; init; }
        public string? SourceUrl { get; init; }
        public string? Detail { get; init; }
    }

    public sealed record PackPlan
    {
        public required PackFormat Format { get; init; }
        public IReadOnlyList<PlannedFile> Files { get; init; } = [];
        public IReadOnlyList<PendingSpec> Pending { get; init; } = [];
        public int Skipped { get; init; }
    }

    public sealed record PackDetection(PackFormat Format, string Prefix);

    public sealed class PackImportException(string message) : InvalidOperationException(message);
}
