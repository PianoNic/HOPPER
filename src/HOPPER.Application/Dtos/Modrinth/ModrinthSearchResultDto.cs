namespace HOPPER.Application.Dtos.Modrinth
{
    public record ModrinthSearchResultDto
    {
        public required IReadOnlyList<ModrinthSearchHitDto> Hits { get; init; }
        public required int Offset { get; init; }
        public required int Limit { get; init; }
        public required int TotalHits { get; init; }
    }
}
