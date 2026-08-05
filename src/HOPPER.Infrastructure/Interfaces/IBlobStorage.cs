namespace HOPPER.Infrastructure.Interfaces
{
    /// <summary>Content-addressed store for mod jars. The bytes decide the path, so an identical
    /// jar uploaded twice occupies one file and every reader that knows the hash can find it.</summary>
    public interface IBlobStorage
    {
        /// <summary>Streams <paramref name="content"/> to a temp file while hashing it, then moves it
        /// to its content address. Returns the hash and byte count. The caller never picks the path —
        /// the content does.</summary>
        Task<(string Sha256, long Size)> SaveAsync(Stream content, CancellationToken cancellationToken = default);

        /// <summary>Opens the blob for reading, or null when it is not stored. The caller owns the
        /// stream; ASP.NET disposes it after File(...).</summary>
        Stream? OpenRead(string sha256);

        bool Exists(string sha256);

        /// <summary>Removes the blob. A blob that is already gone is not an error — delete is called
        /// after the row is committed, so a retry must not fail.</summary>
        void Delete(string sha256);
    }
}
