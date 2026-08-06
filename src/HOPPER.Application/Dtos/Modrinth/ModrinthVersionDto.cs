namespace HOPPER.Application.Dtos.Modrinth
{
    /// <summary>One row in the version picker. The file fields are already resolved down to the
    /// primary jar, so the dashboard never has to reimplement "which of these files is the mod" -
    /// that rule (at most one primary, otherwise the first, never a file with a file_type) lives once,
    /// next to the API models.</summary>
    public record ModrinthVersionDto
    {
        public required string Id { get; init; }
        public required string ProjectId { get; init; }
        public string? Name { get; init; }
        public string? VersionNumber { get; init; }

        /// <summary>release | beta | alpha, as Modrinth publish it.</summary>
        public string? VersionType { get; init; }

        public DateTime? DatePublished { get; init; }
        public required long Downloads { get; init; }
        public required IReadOnlyList<string> GameVersions { get; init; }
        public required IReadOnlyList<string> Loaders { get; init; }

        /// <summary>Null when the version publishes nothing installable. Such a version is still listed
        /// so it is not mysteriously absent, but it cannot be picked.</summary>
        public string? FileName { get; init; }

        public long FileSize { get; init; }

        /// <summary>True when this server already carries this exact version.</summary>
        public required bool Installed { get; init; }
    }
}
