namespace HOPPER.Application.Dtos.Modrinth
{
    /// <summary>One page of search results. <see cref="Limit"/> is echoed back because it may be lower
    /// than what was asked for - Modrinth clamp at 100 and HOPPER clamps before them.</summary>
    public record ModrinthSearchResultDto
    {
        public required IReadOnlyList<ModrinthSearchHitDto> Hits { get; init; }
        public required int Offset { get; init; }
        public required int Limit { get; init; }
        public required int TotalHits { get; init; }
    }
}
