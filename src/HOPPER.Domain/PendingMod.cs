using HOPPER.Domain.Enums;

namespace HOPPER.Domain
{
    /// <summary>A file a pack asked for that HOPPER could not fetch on its own, kept until the admin
    /// supplies the jar or removes the entry. This is Prism Launcher's BlockedModsDialog as a table:
    /// a CurseForge manifest carries nothing but two integers per file, so without an API key every
    /// entry lands here, and even with a key the mods whose authors disabled distribution do.</summary>
    public class PendingMod : BaseEntity
    {
        /// <summary>Foreign key to <see cref="Server"/>.Id. A raw Guid with no navigation property,
        /// matching the house rule that there are zero EF relationships in the model.</summary>
        public required Guid ServerId { get; init; }

        /// <summary>Foreign key to <see cref="ModImport"/>.Id, so the import that produced this row
        /// can show its own pendings.</summary>
        public required Guid ImportId { get; init; }

        public required PendingReason Reason { get; init; }

        /// <summary>Best-effort label for the admin: the CurseForge API's mod name when there is a
        /// key, otherwise an anchor text scraped from modlist.html. A hint only - modlist.html is
        /// not keyed to files[], so it must never be used to join.</summary>
        public string? DisplayName { get; set; }

        /// <summary>The exact jar name, known only when the CurseForge API resolved the entry.</summary>
        public string? FileName { get; set; }

        public int? ProjectId { get; set; }

        public int? FileId { get; set; }

        /// <summary>SHA-1 as published by CurseForge (algo 1). When present, a supplied jar is
        /// verified against it; when null - the keyless case - the admin's assignment is taken at
        /// face value, because there is nothing to check it against. Neither pack format ever
        /// supplies SHA-256, so this is never the blob address.</summary>
        public string? ExpectedSha1 { get; set; }

        /// <summary>The URL that failed, or a project page the admin can open to find the jar.</summary>
        public string? SourceUrl { get; set; }

        /// <summary>One human sentence explaining this specific row, rendered in the pending list.</summary>
        public string? Detail { get; set; }
    }
}
