using HOPPER.Domain.Enums;

namespace HOPPER.Application.Dtos.Imports
{
    /// <summary>A file the admin has to supply by hand. Nearly everything is nullable because the
    /// keyless CurseForge case genuinely knows nothing but two integers - that is not a gap in this
    /// DTO, it is the entire shape of the problem.</summary>
    public record PendingModDto
    {
        public required Guid Id { get; init; }
        public required Guid ImportId { get; init; }
        public required PendingReason Reason { get; init; }

        /// <summary>Best-effort label. A hint for a human, never a key.</summary>
        public string? DisplayName { get; init; }

        /// <summary>Known only when the CurseForge API resolved the entry.</summary>
        public string? FileName { get; init; }

        public int? ProjectId { get; init; }
        public int? FileId { get; init; }

        /// <summary>When present, a supplied jar is verified against it. When null, the admin's
        /// assignment is taken at face value - there is nothing to check it against.</summary>
        public string? ExpectedSha1 { get; init; }

        public string? SourceUrl { get; init; }

        /// <summary>One sentence explaining this row, rendered as-is.</summary>
        public string? Detail { get; init; }

        public required DateTime CreatedAt { get; init; }
    }
}
