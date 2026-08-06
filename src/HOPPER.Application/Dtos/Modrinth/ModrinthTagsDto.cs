namespace HOPPER.Application.Dtos.Modrinth
{
    public record ModrinthTagsDto
    {
        public required IReadOnlyList<string> Loaders { get; init; }

        public required IReadOnlyList<ModrinthGameVersionDto> GameVersions { get; init; }
    }

    public record ModrinthGameVersionDto
    {
        public required string Version { get; init; }

        public required bool Major { get; init; }
    }
}
