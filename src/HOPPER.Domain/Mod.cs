using HOPPER.Domain.Enums;

namespace HOPPER.Domain
{
    /// <summary>One row per jar in one server's distributed set. The rows for a given ServerId are
    /// exactly the entries of that server's manifest, so anything added here is downloaded by every
    /// client of that server and anything removed is deleted from their hopper/ directories.</summary>
    public class Mod : BaseEntity
    {
        /// <summary>Foreign key to <see cref="Server"/>.Id. A raw Guid with no navigation property,
        /// matching the house rule that there are zero EF relationships in the model.
        ///
        /// This column is what scopes a mod to one server. The blob it points at is NOT scoped:
        /// two rows on two different servers may carry the same Sha256 and share one file on disk.
        /// See <see cref="Sha256"/>.</summary>
        public required Guid ServerId { get; init; }

        /// <summary>Filename the client writes into its hopper/ directory, and the key the client
        /// syncs on. Validated at upload against the same rules the Java Syncer.sanitize() enforces
        /// (no separators, no "..", no leading dot, must end in .jar) so a manifest can never be
        /// produced that the client will reject at runtime.</summary>
        public required string FileName { get; init; }

        /// <summary>SHA-256 of the jar, 64 lowercase hex characters. Doubles as the blob's address
        /// on disk and as the last path segment of the download URL handed to the client.
        ///
        /// Not unique, and deliberately not scoped by ServerId: the blob store is global and
        /// content-addressed, so the same jar uploaded to two servers is stored once and referenced
        /// twice. That is also why the orphan check before deleting a blob has to scan Mod rows
        /// across all servers rather than just the one being edited.</summary>
        public required string Sha256 { get; init; }

        public required long Size { get; init; }

        /// <summary>Display name of the admin who uploaded it, from the OIDC "name" claim.
        /// Null when the upload happened outside an authenticated context.</summary>
        public string? UploadedBy { get; set; }

        /// <summary>Where this jar came from. Manual is the default and the only value existing rows
        /// carry: everything uploaded or pack-imported before the Modrinth browser existed is Manual
        /// with every provenance column null. Nothing may read a provenance column without checking
        /// it for null first.
        ///
        /// Provenance is invisible on the wire. The client manifest is still exactly
        /// {"file","url","sha256","size"} for a Modrinth mod and a hand-uploaded one alike; these
        /// columns exist so a server can be exported as a portable pack, nothing else.
        ///
        /// Settable rather than init-only because adopting an existing hand-uploaded row - the same
        /// bytes turning out to be a known Modrinth file - rewrites provenance in place instead of
        /// inserting a duplicate row.</summary>
        public ModSource Source { get; set; } = ModSource.Manual;

        /// <summary>Modrinth base62 project id, for example "u6dRKJwZ". Null unless the provenance
        /// was recorded.
        ///
        /// Deliberately a string and deliberately not shared with PendingMod.ProjectId, which is an
        /// int because CurseForge project ids are numeric. Modrinth's are not, so the two cannot be
        /// the same column.</summary>
        public string? ProjectId { get; set; }

        /// <summary>Modrinth version id, for example "mcC2LhSG". Identifies the exact file that was
        /// downloaded, not the project, which is what makes "is this already installed, and at which
        /// version" answerable without hitting the API.</summary>
        public string? VersionId { get; set; }

        /// <summary>Project title as it read when the mod was added, cached for the dashboard and
        /// for the exported modlist.html. A display value only - nothing ever resolves by it.</summary>
        public string? ProjectName { get; set; }

        /// <summary>The upstream CDN URL the jar was fetched from, written verbatim into an exported
        /// .mrpack's downloads[]. This column is the entire reason a portable pack is possible:
        /// HOPPER's own blob URL is reachable only by a client holding this server's token, so it
        /// must never appear inside an exported pack.</summary>
        public string? DownloadUrl { get; set; }

        /// <summary>SHA-1 as the upstream published it, 40 lowercase hex characters.
        ///
        /// Stored in addition to - never instead of - <see cref="Sha256"/>. Modrinth publishes sha1
        /// and sha512 and never sha256, while the blob store addresses by sha256 and the client wire
        /// format pins it, so a downloaded jar is verified against these two and then hashed a third
        /// time by HOPPER. The pack formats require sha1 and sha512 verbatim.</summary>
        public string? Sha1 { get; set; }

        /// <summary>SHA-512 as the upstream published it, 128 lowercase hex characters. See
        /// <see cref="Sha1"/> for why both live alongside <see cref="Sha256"/>.</summary>
        public string? Sha512 { get; set; }
    }
}
