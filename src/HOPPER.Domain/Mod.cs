using HOPPER.Domain.Enums;

namespace HOPPER.Domain
{
    public class Mod : BaseEntity
    {
        public required Guid ServerId { get; init; }

        public required string FileName { get; init; }

        public required string Sha256 { get; init; }

        public required long Size { get; init; }

        public string? UploadedBy { get; set; }

        public ModSource Source { get; set; } = ModSource.Manual;

        /// <summary>
        /// Which side this jar belongs on. Both is 0 so every row that predates the column keeps
        /// going everywhere, which is what HOPPER did before there was a side at all.
        /// </summary>
        public ModSide Side { get; set; } = ModSide.Both;

        public string? ProjectId { get; set; }

        public string? VersionId { get; set; }

        public string? ProjectName { get; set; }

        public string? DownloadUrl { get; set; }

        public string? Sha1 { get; set; }

        public string? Sha512 { get; set; }

        public string[]? ModIds { get; set; }

        /// <summary>
        /// Where the icon lives on the platform this mod came from, kept beside the other
        /// provenance for the same reason: HOPPER did not make this jar and cannot re-derive it.
        /// Only set for a mod installed through the manager, never for one uploaded by hand.
        /// </summary>
        public string? IconUrl { get; set; }

        /// <summary>
        /// The mod's own icon, in the blob store rather than as a URL. Content-addressed, so the
        /// same icon across servers costs one copy, and served by HOPPER, so a dashboard on a
        /// network that cannot reach Modrinth still shows it.
        /// </summary>
        public string? IconSha256 { get; set; }
    }
}
