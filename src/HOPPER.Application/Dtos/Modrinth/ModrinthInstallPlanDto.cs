using HOPPER.Application.Modrinth;

namespace HOPPER.Application.Dtos.Modrinth
{
    public record ModrinthInstallPlanDto
    {
        public required IReadOnlyList<ModrinthPlanNodeDto> Nodes { get; init; }

        public required IReadOnlyList<ModrinthPlanNodeDto> Optional { get; init; }

        public required IReadOnlyList<ModrinthEmbeddedDto> Embedded { get; init; }

        public required IReadOnlyList<ModrinthIncompatibleDto> Incompatible { get; init; }
        public required IReadOnlyList<ModrinthUnresolvableDto> Unresolvable { get; init; }
        public required IReadOnlyList<string> Warnings { get; init; }

        public required bool Blocked { get; init; }

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

        public required PlanNodeKind Kind { get; init; }

        public required PlanNodeStatus Status { get; init; }

        public required int Depth { get; init; }

        public required IReadOnlyList<string> RequiredBy { get; init; }

        public required bool Pinned { get; init; }

        public required bool Prerelease { get; init; }
    }

    public record ModrinthIncompatibleDto
    {
        public required string ProjectId { get; init; }
        public string? Title { get; init; }

        public required string DeclaredBy { get; init; }

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
