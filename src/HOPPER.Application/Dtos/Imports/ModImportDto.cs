using HOPPER.Domain.Enums;

namespace HOPPER.Application.Dtos.Imports
{
    /// <summary>One import job as the history table shows it. The counters are the whole point: they
    /// are written per file while the job runs, so polling this shows progress rather than a spinner
    /// that ends in a number.</summary>
    public record ModImportDto
    {
        public required Guid Id { get; init; }

        /// <summary>The uploaded filename or the pasted URL, verbatim.</summary>
        public required string SourceName { get; init; }

        public required ImportSourceKind SourceKind { get; init; }

        /// <summary>Unknown until the detector has read the archive, and stays Unknown if detection
        /// itself failed.</summary>
        public required PackFormat Format { get; init; }

        public required ImportStatus Status { get; init; }
        public required int ImportedCount { get; init; }

        /// <summary>Files deliberately not imported: already on this server, or not a mod jar.</summary>
        public required int SkippedCount { get; init; }

        public required int PendingCount { get; init; }
        public required int FailedCount { get; init; }

        /// <summary>Null on a clean run. Carries the reason the job failed, plus per-file error lines.</summary>
        public string? Error { get; init; }

        public DateTime? StartedAt { get; init; }
        public DateTime? CompletedAt { get; init; }
        public string? CreatedBy { get; init; }
        public required DateTime CreatedAt { get; init; }
    }
}
