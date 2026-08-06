using HOPPER.Domain.Enums;

namespace HOPPER.Application.Dtos.Imports
{
    public record PendingModDto
    {
        public required Guid Id { get; init; }
        public required Guid ImportId { get; init; }
        public required PendingReason Reason { get; init; }

        public string? DisplayName { get; init; }

        public string? FileName { get; init; }

        public int? ProjectId { get; init; }
        public int? FileId { get; init; }

        public string? ExpectedSha1 { get; init; }

        public string? SourceUrl { get; init; }

        public string? Detail { get; init; }

        public required DateTime CreatedAt { get; init; }
    }
}
