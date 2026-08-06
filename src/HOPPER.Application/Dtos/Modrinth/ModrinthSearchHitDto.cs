namespace HOPPER.Application.Dtos.Modrinth
{
    public record ModrinthSearchHitDto
    {
        public required string ProjectId { get; init; }
        public string? Slug { get; init; }
        public required string Title { get; init; }
        public string? Description { get; init; }
        public string? Author { get; init; }
        public string? IconUrl { get; init; }
        public required long Downloads { get; init; }
        public required long Follows { get; init; }

        public required IReadOnlyList<string> Categories { get; init; }

        public DateTime? DateModified { get; init; }

        public required bool Installed { get; init; }
    }
}
