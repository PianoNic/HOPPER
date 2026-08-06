namespace HOPPER.Application.Dtos.Servers
{
    /// <summary>Admin view of one server. Deliberately carries no token: the token is a credential
    /// that opens that server's whole mod set, and a list endpoint the dashboard calls on every page
    /// load is the wrong place to hand it out. It has its own endpoint and its own DTO
    /// (<see cref="ServerTokenDto"/>) so revealing it is always a deliberate act.</summary>
    public record ServerDto
    {
        public required Guid Id { get; init; }
        public required string Name { get; init; }

        /// <summary>Names the generated jar (&lt;slug&gt;-hopper.jar), which is why it is unique and
        /// filename-safe rather than merely a display convenience.</summary>
        public required string Slug { get; init; }

        /// <summary>Mods on this server. Counted rather than embedded - the list page shows a number
        /// and the mods page fetches the rows.</summary>
        public required int ModCount { get; init; }

        public required int ClientCount { get; init; }
        public required DateTime CreatedAt { get; init; }
    }
}
