using HOPPER.Domain.Enums;

namespace HOPPER.Application.Dtos.Mods
{
    /// <summary>Admin view of one distributed jar. Unlike the manifest DTOs these names are not a
    /// fixed contract - the dashboard's TypeScript client is generated from the same OpenAPI
    /// document, so whatever the serializer emits is what the client expects.</summary>
    public record ModDto
    {
        public required Guid Id { get; init; }
        public required string FileName { get; init; }
        public required string Sha256 { get; init; }
        public required long Size { get; init; }
        public string? UploadedBy { get; init; }
        public required DateTime CreatedAt { get; init; }

        /// <summary>0 Manual, 1 Modrinth, 2 CurseForge. Mirrored by number on the frontend, the same
        /// way the four import enums already are.</summary>
        public required ModSource Source { get; init; }

        public string? ProjectId { get; init; }
        public string? VersionId { get; init; }
        public string? ProjectName { get; init; }

        /// <summary>The upstream CDN URL, shown so an admin can see where a jar came from. Note what is
        /// NOT here: Sha1 and Sha512. Nothing in the dashboard renders them, and they exist only so the
        /// pack formats can be written, so they stay in the database.</summary>
        public string? DownloadUrl { get; init; }
    }
}
