using HOPPER.Domain;

namespace HOPPER.Application.Modrinth
{
    public enum PlanNodeKind
    {
        Root = 0,

        Required = 1,

        Optional = 2,
    }

    public enum PlanNodeStatus
    {
        New = 0,

        AlreadyInstalled = 1,

        OtherVersionInstalled = 2,

        FileNameTaken = 3,
    }

    public sealed class PlanNode
    {
        public required string ProjectId { get; init; }
        public string? ProjectSlug { get; set; }
        public string? ProjectTitle { get; set; }
        public string? IconUrl { get; set; }
        public required string VersionId { get; init; }
        public string? VersionNumber { get; init; }
        public string? VersionType { get; init; }
        public required string FileName { get; init; }
        public long FileSize { get; init; }
        public required string DownloadUrl { get; init; }
        public string? Sha1 { get; init; }
        public string? Sha512 { get; init; }
        public PlanNodeKind Kind { get; set; }
        public PlanNodeStatus Status { get; set; }
        public int Depth { get; init; }

        public List<string> RequiredBy { get; } = [];

        public bool Pinned { get; init; }

        public bool Prerelease { get; init; }

        public string DisplayName => ProjectTitle ?? ProjectSlug ?? FileName;
    }

    public sealed record IncompatibleNote(string ProjectId, string? Title, string DeclaredBy, bool Applies);

    public sealed record UnresolvableNote(string Name, string Reason, string RequestedBy);

    public sealed record EmbeddedNote(string ProjectId, string? Title, string BundledBy);

    public sealed record InstalledMod(string? ProjectId, string? VersionId, string FileName);

    public sealed record ResolveRequest
    {
        public required IReadOnlyList<string> RootVersionIds { get; init; }

        public required string Loader { get; init; }
        public required string GameVersion { get; init; }
        public IReadOnlyList<InstalledMod> Installed { get; init; } = [];
    }

    public sealed record ResolveResult
    {
        public required IReadOnlyList<PlanNode> Nodes { get; init; }

        public required IReadOnlyList<PlanNode> Optional { get; init; }

        public required IReadOnlyList<EmbeddedNote> Embedded { get; init; }
        public required IReadOnlyList<IncompatibleNote> Incompatible { get; init; }
        public required IReadOnlyList<UnresolvableNote> Unresolvable { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }

        public required bool Blocked { get; init; }

        public int ApiCalls { get; init; }
    }

    public sealed class ResolveBudgetExceededException(string message) : RuleViolationException(message);

    public sealed class IncompatibleModException(string message) : InvalidOperationException(message);
}
