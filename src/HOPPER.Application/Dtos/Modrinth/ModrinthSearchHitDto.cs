namespace HOPPER.Application.Dtos.Modrinth
{
    /// <summary>One card in the browser's result grid.
    ///
    /// Flattened out of Modrinth's own hit shape rather than passed through: a hit carries a gallery,
    /// a colour, an organization and a license the dashboard never renders, and forwarding a
    /// third-party schema verbatim would make it part of HOPPER's generated client.</summary>
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

        /// <summary>Loaders are folded into categories by the search endpoint, so this list holds both.
        /// It is display only - the filter is applied server-side as a facet.</summary>
        public required IReadOnlyList<string> Categories { get; init; }

        public DateTime? DateModified { get; init; }

        /// <summary>True when this server already carries a mod with this project id. Computed here
        /// rather than in the dashboard so a results page needs no second request to answer it.</summary>
        public required bool Installed { get; init; }
    }
}
