using HOPPER.Domain.Enums;

namespace HOPPER.Domain
{
    /// <summary>One row per pack import attempt. It exists so the dashboard has something to poll:
    /// an import of a 340-file modpack runs for minutes on a background worker, and the counters
    /// below are written per file rather than once at the end, so progress is real rather than
    /// inferred.</summary>
    public class ModImport : BaseEntity
    {
        /// <summary>Foreign key to <see cref="Server"/>.Id. A raw Guid with no navigation property,
        /// matching the house rule that there are zero EF relationships in the model.</summary>
        public required Guid ServerId { get; init; }

        /// <summary>The uploaded filename, or the URL that was pasted. Shown verbatim in the
        /// history table, so it is whatever the admin actually supplied.</summary>
        public required string SourceName { get; init; }

        public required ImportSourceKind SourceKind { get; init; }

        /// <summary>Written once the detector has read the archive's central directory. Stays
        /// Unknown when detection itself failed.</summary>
        public PackFormat Format { get; set; }

        public ImportStatus Status { get; set; }

        /// <summary>Files that became Mod rows on this server.</summary>
        public int ImportedCount { get; set; }

        /// <summary>Files deliberately not imported: already present on this server (imports are
        /// re-runnable), or not a mod jar at all (resource packs, shaders, datapacks).</summary>
        public int SkippedCount { get; set; }

        /// <summary>Files that produced a <see cref="PendingMod"/> the admin must resolve by hand.</summary>
        public int PendingCount { get; set; }

        /// <summary>Files rejected outright - an invalid jar name, or a per-file error that is not
        /// worth asking the admin about.</summary>
        public int FailedCount { get; set; }

        /// <summary>Human-readable reason the import as a whole failed, plus any accumulated
        /// per-file error lines. Null on a clean run.</summary>
        public string? Error { get; set; }

        public DateTime? StartedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        /// <summary>Display name of the admin who started it, from the OIDC "name" claim. Copied
        /// onto every Mod row the import produces.</summary>
        public string? CreatedBy { get; set; }
    }
}
