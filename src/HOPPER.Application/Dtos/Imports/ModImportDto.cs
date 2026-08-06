using HOPPER.Domain.Enums;

namespace HOPPER.Application.Dtos.Imports
{
    public record ModImportDto
    {
        public required Guid Id { get; init; }

        public required string SourceName { get; init; }

        public required ImportSourceKind SourceKind { get; init; }

        public required PackFormat Format { get; init; }

        public required ImportStatus Status { get; init; }
        public required int ImportedCount { get; init; }

        public required int SkippedCount { get; init; }

        public required int PendingCount { get; init; }
        public required int FailedCount { get; init; }

        public string? Error { get; init; }

        public DateTime? StartedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
        public string? CreatedBy { get; init; }
        public required DateTime CreatedAt { get; init; }
    }
}
