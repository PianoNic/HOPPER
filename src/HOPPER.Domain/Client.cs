using HOPPER.Domain.Enums;

namespace HOPPER.Domain
{
    public class Client : BaseEntity
    {
        public required Guid ServerId { get; init; }

        public required string ClientId { get; set; }

        public string? Username { get; set; }

        public SyncSide Side { get; set; } = SyncSide.Client;

        public required DateTime LastSeenAt { get; set; }

        public string? LastIpAddress { get; set; }
    }
}
