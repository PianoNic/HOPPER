using HOPPER.Domain.Enums;

namespace HOPPER.Application.Dtos.Mods
{
    public record ModDto
    {
        public required Guid Id { get; init; }
        public required string FileName { get; init; }
        public required string Sha256 { get; init; }
        public required long Size { get; init; }
        public string? UploadedBy { get; init; }
        public required DateTime CreatedAt { get; init; }

        public required ModSource Source { get; init; }

        public string? ProjectId { get; init; }
        public string? VersionId { get; init; }
        public string? ProjectName { get; init; }

        public string? DownloadUrl { get; init; }
    }
}
