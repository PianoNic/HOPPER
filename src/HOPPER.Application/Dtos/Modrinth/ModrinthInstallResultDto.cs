using HOPPER.Application.Dtos.Mods;

namespace HOPPER.Application.Dtos.Modrinth
{
    /// <summary>One item of an install request. <see cref="Replace"/> defaults to false and has to be
    /// ticked per row in the plan dialog: replacing is a deliberate act, and the plan showed exactly
    /// which rows it would affect.</summary>
    public record ModrinthInstallItemDto
    {
        public required string VersionId { get; init; }
        public bool Replace { get; init; }
    }

    /// <summary>What actually happened. Five outcomes rather than a count, because a batch where one
    /// jar's hash did not match is a partial success that has to be reportable per row.</summary>
    public record ModrinthInstallResultDto
    {
        public required IReadOnlyList<ModDto> Installed { get; init; }

        /// <summary>Rows that already held these exact bytes under another name and were claimed
        /// rather than duplicated.</summary>
        public required IReadOnlyList<ModrinthAdoptedDto> Adopted { get; init; }

        /// <summary>Rows whose old version was deleted to make room, because Replace was ticked.</summary>
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
