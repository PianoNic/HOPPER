using HOPPER.Domain.Enums;

namespace HOPPER.Domain
{
    public class Client : BaseEntity
    {
        public required Guid ServerId { get; init; }

        public required string ClientId { get; init; }

        public string? Username { get; set; }

        /// <summary>
        /// Which side reported. Client is 0, so every row that predates the column reads back as
        /// what it in fact was - there were no dedicated servers before this existed.
        /// </summary>
        public SyncSide Side { get; set; } = SyncSide.Client;

        public required DateTime LastSeenAt { get; set; }

        public string? LastIpAddress { get; set; }
    }
}
