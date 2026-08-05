namespace HOPPER.Domain
{
    /// <summary>A single jar a client reported having on disk. The set of rows for one client is
    /// replaced wholesale on each report rather than appended to: the dashboard only ever asks
    /// "what does this client have right now", and an append-only log would grow without bound
    /// for no consumer.</summary>
    public class ClientReportedMod : BaseEntity
    {
        /// <summary>Foreign key to <see cref="Client"/>.Id. A raw Guid with no navigation property,
        /// matching the house rule that there are zero EF relationships in the model.</summary>
        public required Guid ClientId { get; init; }

        public required string FileName { get; init; }

        /// <summary>SHA-256 the client computed over the jar on its own disk. Compared against the
        /// Mod table to decide whether the client is in sync or carrying a jar we never sent.</summary>
        public required string Sha256 { get; init; }
    }
}
