namespace HOPPER.Application.Modrinth
{
    /// <summary>Why a node is in the plan.</summary>
    public enum PlanNodeKind
    {
        /// <summary>The admin picked it, or ticked it on.</summary>
        Root = 0,

        /// <summary>Pulled in transitively because something else declares it required.</summary>
        Required = 1,

        /// <summary>Offered, unticked. Its own dependency graph is deliberately NOT walked until it
        /// is ticked, at which point the whole resolve re-runs with it as a root - that is what makes
        /// "nothing arrives that the admin did not see" true for optionals too.</summary>
        Optional = 2,
    }

    /// <summary>How a node lines up against what the server already carries.</summary>
    public enum PlanNodeStatus
    {
        New = 0,

        /// <summary>Same project, same version. Nothing to do.</summary>
        AlreadyInstalled = 1,

        /// <summary>Same project at a different version. Defaults to SKIP, never to replace: an
        /// upgrade is a deliberate act, and (ServerId, FileName) is unique so a blind insert would
        /// conflict anyway.</summary>
        OtherVersionInstalled = 2,

        /// <summary>A different mod already occupies this filename on this server.</summary>
        FileNameTaken = 3,
    }

    /// <summary>One mod the plan would add, with everything the dialog needs to name it and everything
    /// the install needs to fetch it.</summary>
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

        /// <summary>Project titles that asked for this one, for the "required by X" caption. Empty on
        /// a root.</summary>
        public List<string> RequiredBy { get; } = [];

        /// <summary>The dependency named an exact version id, so no version was chosen on its
        /// behalf.</summary>
        public bool Pinned { get; init; }

        /// <summary>No release matched the server's loader and Minecraft version, so a beta or alpha
        /// was taken. Surfaced rather than silently accepted.</summary>
        public bool Prerelease { get; init; }

        public string DisplayName => ProjectTitle ?? ProjectSlug ?? FileName;
    }

    /// <summary>A declared incompatibility. <see cref="Applies"/> is the difference between a warning
    /// and a refusal: a mod declaring another one incompatible matters only when that other one is
    /// actually here.</summary>
    public sealed record IncompatibleNote(string ProjectId, string? Title, string DeclaredBy, bool Applies);

    /// <summary>Something the plan could not turn into a downloadable file, shown rather than
    /// swallowed. A dependency with a null project_id is not resolvable through the API at all - it
    /// still has to reach the admin, and it does not fail the plan.</summary>
    public sealed record UnresolvableNote(string Name, string Reason, string RequestedBy);

    /// <summary>A library bundled inside its parent's jar. Never added: shipping it separately means
    /// the same classes twice, which Forge may reject outright.</summary>
    public sealed record EmbeddedNote(string ProjectId, string? Title, string BundledBy);

    /// <summary>What the server already carries, as the resolver needs it. Passed in rather than read
    /// from the database, which is what keeps the resolver a pure function of its inputs.</summary>
    public sealed record InstalledMod(string? ProjectId, string? VersionId, string FileName);

    public sealed record ResolveRequest
    {
        /// <summary>Everything the admin selected, including any optional that has been ticked on. A
        /// ticked optional is a root, not an optional - that is precisely why ticking one re-resolves
        /// and shows whatever IT drags in before anything is written.</summary>
        public required IReadOnlyList<string> RootVersionIds { get; init; }

        public required string Loader { get; init; }
        public required string GameVersion { get; init; }
        public IReadOnlyList<InstalledMod> Installed { get; init; } = [];
    }

    public sealed record ResolveResult
    {
        /// <summary>Roots plus every transitive required mod. These are what install writes.</summary>
        public required IReadOnlyList<PlanNode> Nodes { get; init; }

        /// <summary>Offered but unticked, resolved far enough to be named and sized.</summary>
        public required IReadOnlyList<PlanNode> Optional { get; init; }

        public required IReadOnlyList<EmbeddedNote> Embedded { get; init; }
        public required IReadOnlyList<IncompatibleNote> Incompatible { get; init; }
        public required IReadOnlyList<UnresolvableNote> Unresolvable { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }

        /// <summary>True when at least one incompatibility actually applies. The plan still returns in
        /// full - the admin should see why - but install refuses.</summary>
        public required bool Blocked { get; init; }

        public int ApiCalls { get; init; }
    }

    /// <summary>Derives from ArgumentException so the existing 400 catch in the pipeline covers it: a
    /// tree this large is a request that cannot be served, not a fault.</summary>
    public sealed class ResolveBudgetExceededException(string message) : ArgumentException(message);

    /// <summary>Install refuses rather than writing a set that cannot load. Mapped to 409.</summary>
    public sealed class IncompatibleModException(string message) : InvalidOperationException(message);
}
