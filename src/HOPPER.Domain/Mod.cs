namespace HOPPER.Domain
{
    /// <summary>One row per jar in the distributed set. The rows of this table are exactly the
    /// entries of the manifest the Forge locator consumes, so anything added here is downloaded by
    /// every client and anything removed is deleted from every client's hopper/ directory.</summary>
    public class Mod : BaseEntity
    {
        /// <summary>Filename the client writes into its hopper/ directory, and the key the client
        /// syncs on. Validated at upload against the same rules the Java Syncer.sanitize() enforces
        /// (no separators, no "..", no leading dot, must end in .jar) so a manifest can never be
        /// produced that the client will reject at runtime.</summary>
        public required string FileName { get; init; }

        /// <summary>SHA-256 of the jar, 64 lowercase hex characters. Doubles as the blob's address
        /// on disk and as the last path segment of the download URL handed to the client.</summary>
        public required string Sha256 { get; init; }

        public required long Size { get; init; }

        /// <summary>Display name of the admin who uploaded it, from the OIDC "name" claim.
        /// Null when the upload happened outside an authenticated context.</summary>
        public string? UploadedBy { get; set; }
    }
}
