namespace HOPPER.Domain
{
    /// <summary>One row per known game install. Created and refreshed by POST /api/clients/report;
    /// there is no registration step, so a client exists exactly when it has reported once.</summary>
    public class Client : BaseEntity
    {
        /// <summary>Foreign key to <see cref="Server"/>.Id, resolved from the bearer token the
        /// report was made with. A raw Guid with no navigation property, matching the house rule
        /// that there are zero EF relationships in the model.</summary>
        public required Guid ServerId { get; init; }

        /// <summary>The client-generated identifier persisted in the player's hopper/client-id file.
        /// Stable across launches and username changes, which is why it - not the username - is the
        /// natural key for a client.
        ///
        /// Unique only within a server: it is a random UUID minted per game directory, so a player
        /// who joins two HOPPER servers from two instances has two of them, and nothing stops the
        /// same directory reporting to a second server. The natural key is (ServerId, ClientId).</summary>
        public required string ClientId { get; init; }

        /// <summary>Minecraft username as of the last report, or null when the client had none to
        /// report - a dedicated server, or a launcher started without --username. Mutable: players
        /// rename, and a client can gain or lose a username between launches.</summary>
        public string? Username { get; set; }

        public required DateTime LastSeenAt { get; set; }

        /// <summary>Remote address of the last report. Only used to tell two installs apart in the
        /// dashboard when a friend group shares usernames.</summary>
        public string? LastIpAddress { get; set; }
    }
}
