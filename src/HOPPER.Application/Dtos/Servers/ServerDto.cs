using HOPPER.Domain.Enums;

namespace HOPPER.Application.Dtos.Servers
{
    public record ServerDto
    {
        public required Guid Id { get; init; }
        public required string Name { get; init; }

        public required string Slug { get; init; }

        public required int ModCount { get; init; }

        public required int ClientCount { get; init; }
        public required DateTime CreatedAt { get; init; }

        public string? MinecraftVersion { get; init; }

        public required ModLoader Loader { get; init; }

        public string? LoaderVersion { get; init; }

        public string? IconSha256 { get; init; }
    }
}
