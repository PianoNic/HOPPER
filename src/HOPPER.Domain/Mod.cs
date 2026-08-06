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
    }
}
