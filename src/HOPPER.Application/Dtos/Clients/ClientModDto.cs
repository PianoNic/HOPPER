namespace HOPPER.Application.Dtos.Clients
{
    /// <summary>One jar a client reported, annotated with whether the server recognises it.</summary>
    public record ClientModDto
    {
        public required string FileName { get; init; }
        public required string Sha256 { get; init; }

        /// <summary>False when no Mod row carries this hash — i.e. the player is running a jar we
        /// never sent. Drives the "drift" badge on the Clients page.</summary>
        public required bool Known { get; init; }
    }
}
