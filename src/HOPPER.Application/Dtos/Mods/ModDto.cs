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

        /// <summary>Fetch it from /api/icons/{sha256}. Null when this mod has no icon.</summary>
        public string? IconSha256 { get; init; }

        /// <summary>The platform's own icon, for a mod installed through the manager.</summary>
        public string? IconUrl { get; init; }
    }
}
