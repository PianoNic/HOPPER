namespace HOPPER.Application.Dtos.Modrinth
{
    public record ModrinthVersionDto
    {
        public required string Id { get; init; }
        public required string ProjectId { get; init; }
        public string? Name { get; init; }
        public string? VersionNumber { get; init; }

        public string? VersionType { get; init; }

        public DateTime? DatePublished { get; init; }
        public required long Downloads { get; init; }
        public required IReadOnlyList<string> GameVersions { get; init; }
        public required IReadOnlyList<string> Loaders { get; init; }

        public string? FileName { get; init; }

        public long FileSize { get; init; }

        public required bool Installed { get; init; }
    }
}
