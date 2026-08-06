namespace HOPPER.Application.Dtos.Modrinth
{
    /// <summary>Populates the browser's two filter dropdowns.</summary>
    public record ModrinthTagsDto
    {
        /// <summary>Loader names only. Modrinth return a full inline SVG icon per entry, which is
        /// dropped at the parse boundary rather than forwarded - it is tens of kilobytes of markup the
        /// dashboard has no use for.</summary>
        public required IReadOnlyList<string> Loaders { get; init; }

        /// <summary>Release versions only, newest first. The unfiltered list is 905 entries, almost all
        /// of them snapshots.</summary>
        public required IReadOnlyList<ModrinthGameVersionDto> GameVersions { get; init; }
    }

    public record ModrinthGameVersionDto
    {
        public required string Version { get; init; }

        /// <summary>Marks the head of a version line (1.20, 1.21), for grouping in a long dropdown.</summary>
        public required bool Major { get; init; }
    }
}
