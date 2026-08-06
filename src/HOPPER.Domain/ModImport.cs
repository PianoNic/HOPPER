using HOPPER.Domain.Enums;

namespace HOPPER.Domain
{
    public class ModImport : BaseEntity
    {
        public required Guid ServerId { get; init; }

        public required string SourceName { get; init; }

        public required ImportSourceKind SourceKind { get; init; }

        public PackFormat Format { get; set; }

        public ImportStatus Status { get; set; }

        public int ImportedCount { get; set; }

        public int SkippedCount { get; set; }

        public int PendingCount { get; set; }

        public int FailedCount { get; set; }

        public string? Error { get; set; }

        public DateTime? StartedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public string? CreatedBy { get; set; }
    }
}
