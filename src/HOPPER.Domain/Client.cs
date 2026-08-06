namespace HOPPER.Domain
{
    public class Client : BaseEntity
    {
        public required Guid ServerId { get; init; }

        public required string ClientId { get; init; }

        public string? Username { get; set; }

        public required DateTime LastSeenAt { get; set; }

        public string? LastIpAddress { get; set; }
    }
}
