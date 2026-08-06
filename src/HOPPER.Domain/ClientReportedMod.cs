namespace HOPPER.Domain
{
    public class ClientReportedMod : BaseEntity
    {
        public required Guid ClientId { get; init; }

        public required string FileName { get; init; }

        public required string Sha256 { get; init; }
    }
}
