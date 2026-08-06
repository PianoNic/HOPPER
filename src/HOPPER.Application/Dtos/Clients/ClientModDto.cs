namespace HOPPER.Application.Dtos.Clients
{
    public record ClientModDto
    {
        public required string FileName { get; init; }
        public required string Sha256 { get; init; }

        public required bool Known { get; init; }
    }
}
