namespace HOPPER.Application.Dtos.Modrinth
{
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
    }
}
