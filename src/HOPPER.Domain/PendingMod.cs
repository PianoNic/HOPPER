using HOPPER.Domain.Enums;

namespace HOPPER.Domain
{
    public class PendingMod : BaseEntity
    {
        public required Guid ServerId { get; init; }

        public required Guid ImportId { get; init; }

        public required PendingReason Reason { get; init; }

        public string? DisplayName { get; set; }

        public string? FileName { get; set; }

        public int? ProjectId { get; set; }

        public int? FileId { get; set; }

        public string? ExpectedSha1 { get; set; }

        public string? SourceUrl { get; set; }

        public string? Detail { get; set; }
    }
}
