namespace HOPPER.Application.Dtos.Clients
{
    /// <summary>Admin view of one known game install plus the jar set it last reported.</summary>
    public record ClientDto
    {
        public required Guid Id { get; init; }

        /// <summary>The client-generated id from the player's hopper/client-id file, not our row id.
        /// Surfaced because it is what a player can read off their own disk when asking for help.</summary>
        public required string ClientId { get; init; }

        /// <summary>Null when the client reported no username (dedicated server, or a launcher that
        /// passes none). The dashboard renders a placeholder rather than hiding the row.</summary>
        public string? Username { get; init; }
        public required DateTime LastSeenAt { get; init; }
        public string? LastIpAddress { get; init; }
        public required IReadOnlyList<ClientModDto> Mods { get; init; }
        public required DateTime CreatedAt { get; init; }
    }
}
