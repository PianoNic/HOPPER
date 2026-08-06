using HOPPER.Application.Dtos.Mods;

namespace HOPPER.Application.Dtos.Modrinth
{
    public record ModrinthInstallItemDto
    {
        public required string VersionId { get; init; }
        public bool Replace { get; init; }
    }

    public record ModrinthInstallResultDto
    {
        public required IReadOnlyList<ModDto> Installed { get; init; }

        public required IReadOnlyList<ModrinthAdoptedDto> Adopted { get; init; }

        public required IReadOnlyList<ModDto> Replaced { get; init; }

        public required IReadOnlyList<ModrinthSkippedDto> Skipped { get; init; }
        public required IReadOnlyList<ModrinthFailedDto> Failed { get; init; }
    }

    public record ModrinthAdoptedDto
    {
        public required ModDto Mod { get; init; }
        public required string Message { get; init; }
    }

    public record ModrinthSkippedDto
    {
        public required string Name { get; init; }
        public required string Reason { get; init; }
    }

    public record ModrinthFailedDto
    {
        public required string Name { get; init; }
        public required string Error { get; init; }
    }
}
