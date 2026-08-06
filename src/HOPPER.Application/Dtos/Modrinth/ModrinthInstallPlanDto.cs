using HOPPER.Application.Modrinth;

namespace HOPPER.Application.Dtos.Modrinth
{
    /// <summary>Everything the admin must see before anything is written. This DTO is the contract
    /// behind the rule that nothing arrives unseen: install takes version ids and resolves nothing
    /// further, so the set named here is exactly the set that lands.</summary>
    public record ModrinthInstallPlanDto
    {
        /// <summary>What the admin picked plus every transitive required mod. These are what install
        /// writes.</summary>
        public required IReadOnlyList<ModrinthPlanNodeDto> Nodes { get; init; }

        /// <summary>Offered, unticked. Ticking one re-runs the plan with it as a root, so whatever IT
        /// requires appears in <see cref="Nodes"/> before the admin can confirm.</summary>
        public required IReadOnlyList<ModrinthPlanNodeDto> Optional { get; init; }

        /// <summary>Bundled inside a parent jar. Informational: adding them would ship the same
        /// classes twice.</summary>
        public required IReadOnlyList<ModrinthEmbeddedDto> Embedded { get; init; }

        public required IReadOnlyList<ModrinthIncompatibleDto> Incompatible { get; init; }
        public required IReadOnlyList<ModrinthUnresolvableDto> Unresolvable { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }

        /// <summary>True when an incompatibility actually applies. The plan is still returned in full
        /// so the admin can see why, but install refuses with 409 if asked anyway.</summary>
        public required bool Blocked { get; init; }

        /// <summary>Count and byte total of everything with status New, so the confirm button can
        /// state the number the admin is agreeing to.</summary>
        public required int AddCount { get; init; }

        public required long AddSize { get; init; }
    }

    public record ModrinthPlanNodeDto
    {
        public required string ProjectId { get; init; }
        public string? Slug { get; init; }
        public required string Title { get; init; }
        public string? IconUrl { get; init; }
        public required string VersionId { get; init; }
        public string? VersionNumber { get; init; }
        public string? VersionType { get; init; }
        public required string FileName { get; init; }
        public required long FileSize { get; init; }

        /// <summary>0 Root, 1 Required, 2 Optional. Mirrored by number on the frontend.</summary>
        public required PlanNodeKind Kind { get; init; }

        /// <summary>0 New, 1 AlreadyInstalled, 2 OtherVersionInstalled, 3 FileNameTaken.</summary>
        public required PlanNodeStatus Status { get; init; }

        public required int Depth { get; init; }

        /// <summary>Project titles that asked for this one. Empty on a root.</summary>
        public required IReadOnlyList<string> RequiredBy { get; init; }

        /// <summary>The dependency named an exact version, so nothing was chosen on its behalf.</summary>
        public required bool Pinned { get; init; }

        /// <summary>No release matched, so a beta or alpha was taken.</summary>
        public required bool Prerelease { get; init; }
    }

    public record ModrinthIncompatibleDto
    {
        public required string ProjectId { get; init; }
        public string? Title { get; init; }

        /// <summary>The mod that declares the incompatibility.</summary>
        public required string DeclaredBy { get; init; }

        /// <summary>False means the named mod is not here, so this is a warning rather than a block.</summary>
        public required bool Applies { get; init; }
    }

    public record ModrinthUnresolvableDto
    {
        public required string Name { get; init; }
        public required string Reason { get; init; }
        public required string RequestedBy { get; init; }
    }

    public record ModrinthEmbeddedDto
    {
        public required string ProjectId { get; init; }
        public string? Title { get; init; }
        public required string BundledBy { get; init; }
    }
}
