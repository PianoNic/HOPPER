using HOPPER.Domain.Enums;

namespace HOPPER.Application.Imports
{
    /// <summary>One jar the pack asked for, resolved as far as the planner could take it without
    /// touching the network. Exactly one of <see cref="ZipEntry"/> and <see cref="Downloads"/> is
    /// populated: the bytes are either already in the archive the admin handed us, or they are behind
    /// a URL the importer has to fetch and then verify.</summary>
    public sealed record PlannedFile
    {
        /// <summary>Basename only. Still unvalidated here - the importer runs it through
        /// ModFileNameValidator before it can become a row, because a pack's index is as untrusted as
        /// any other input.</summary>
        public required string FileName { get; init; }

        /// <summary>Full path of the entry inside the staged archive, or null for a download.</summary>
        public string? ZipEntry { get; init; }

        /// <summary>Mirrors of the same file, in the order the index listed them. Tried in turn.</summary>
        public IReadOnlyList<Uri> Downloads { get; init; } = [];

        /// <summary>Integrity hashes as the pack declared them. Neither format ever publishes SHA-256,
        /// so these are only ever checks - the blob address is always computed locally.</summary>
        public string? Sha512 { get; init; }

        public string? Sha1 { get; init; }

        public long? Size { get; init; }
    }

    /// <summary>A file the pack asked for that could not be fetched, described well enough for the
    /// admin to go and find it. Becomes a PendingMod row verbatim.</summary>
    public sealed record PendingSpec
    {
        public required PendingReason Reason { get; init; }
        public string? DisplayName { get; init; }
        public string? FileName { get; init; }
        public int? ProjectId { get; init; }
        public int? FileId { get; init; }
        public string? ExpectedSha1 { get; init; }
        public string? SourceUrl { get; init; }
        public string? Detail { get; init; }
    }

    /// <summary>What a planner made of one archive. Skipped counts the files the pack listed that
    /// HOPPER deliberately does not distribute - resource packs, shaders, datapacks - so the admin
    /// can see that a 340-file pack yielding 300 mods lost nothing by accident.</summary>
    public sealed record PackPlan
    {
        public required PackFormat Format { get; init; }
        public IReadOnlyList<PlannedFile> Files { get; init; } = [];
        public IReadOnlyList<PendingSpec> Pending { get; init; } = [];
        public int Skipped { get; init; }
    }

    /// <summary>The archive shape and the prefix every path in it sits under. The prefix is non-empty
    /// only for a Prism export that was zipped one directory deep.</summary>
    public sealed record PackDetection(PackFormat Format, string Prefix);

    /// <summary>The archive as a whole cannot be imported - it is not a pack, or it is a pack of a
    /// kind HOPPER does not read. Distinct from a per-file failure, which never stops an import.</summary>
    public sealed class PackImportException(string message) : InvalidOperationException(message);
}
