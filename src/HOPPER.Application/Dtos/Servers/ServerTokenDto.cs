namespace HOPPER.Application.Dtos.Servers
{
    public record ServerTokenDto
    {
        public required Guid ServerId { get; init; }

        public required string Token { get; init; }
    }
}
