namespace HOPPER.Application.Dtos.Modrinth
{
    /// <summary>One project's detail panel. Note the field names against
    /// <see cref="ModrinthSearchHitDto"/>: a project's Loaders are a real, separate field and its
    /// GameVersions are Minecraft versions, where a hit folds loaders into categories and calls its
    /// Minecraft versions "versions". They are kept apart deliberately.</summary>
    public record ModrinthProjectDto
    {
        public required string Id { get; init; }
        public string? Slug { get; init; }
        public required string Title { get; init; }
        public string? Description { get; init; }
        public string? Body { get; init; }
        public string? IconUrl { get; init; }
        public string? SourceUrl { get; init; }
        public string? IssuesUrl { get; init; }
        public required long Downloads { get; init; }
        public required long Followers { get; init; }
        public required IReadOnlyList<string> Categories { get; init; }
        public required IReadOnlyList<string> Loaders { get; init; }
        public required IReadOnlyList<string> GameVersions { get; init; }
    }
}
