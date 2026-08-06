namespace HOPPER.Application.Dtos.Clients
{
    public record ClientDto
    {
        public required Guid Id { get; init; }

        public required string ClientId { get; init; }

        public string? Username { get; init; }
        public required DateTime LastSeenAt { get; init; }
        public string? LastIpAddress { get; init; }
        public required IReadOnlyList<ClientModDto> Mods { get; init; }
        public required DateTime CreatedAt { get; init; }
    }
}
