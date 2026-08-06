namespace HOPPER.Domain.Enums
{
    /// <summary>Why a file the pack asked for could not be stored automatically, and therefore what
    /// the admin has to do about it. Persisted as an int.</summary>
    public enum PendingReason
    {
        /// <summary>A CurseForge projectID/fileID pair with no CurseForge:ApiKey configured. Nothing
        /// about the file is knowable offline - not its name, not its hash - so the admin supplies
        /// the jar and asserts which entry it satisfies.</summary>
        NoApiKey = 0,

        /// <summary>Resolved through the CurseForge API, but the author set allowModDistribution to
        /// false so the API returned no download URL. This is the genuine "blocked mod" case, and it
        /// stays pending even with a key.</summary>
        Blocked = 1,

        /// <summary>Every mirror in downloads[] failed, or the host was not on the allow-list.</summary>
        DownloadFailed = 2,

        /// <summary>The bytes arrived but did not match the hash the pack index declared, so the
        /// file is not what the pack described and must never become a Mod row.</summary>
        HashMismatch = 3,
    }
}
