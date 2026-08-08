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

        public ModSide Side { get; set; } = ModSide.Both;

        public string? ProjectId { get; set; }

        public string? VersionId { get; set; }

        public string? ProjectName { get; set; }

        public string? DownloadUrl { get; set; }

        public string? Sha1 { get; set; }

        public string? Sha512 { get; set; }

        /// When Modrinth was last asked what this jar is, so a jar it does not publish is not
        /// asked about on every start.
        public DateTime? ProvenanceCheckedAt { get; set; }

        public string[]? ModIds { get; set; }

        public string[]? RequiredMods { get; set; }

        /// Mod ids shipped inside this jar. Loaders extract them globally, so they satisfy any mod.
        public string[]? BundledMods { get; set; }

        public string? IconUrl { get; set; }

        public string? IconSha256 { get; set; }
    }
}
