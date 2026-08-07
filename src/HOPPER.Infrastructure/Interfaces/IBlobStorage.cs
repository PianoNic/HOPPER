namespace HOPPER.Infrastructure.Interfaces
{
    public sealed record StagedBlob(string Sha256, long Size, string TempPath);

    public sealed record StoredBlob(string Sha256, DateTime LastWriteUtc);

    public sealed record ScratchFile(string Path, DateTime LastWriteUtc);

    public interface IBlobStorage
    {
        Task<StagedBlob> StageAsync(Stream content, long maxBytes, CancellationToken cancellationToken = default);

        void Promote(StagedBlob staged);

        void Discard(StagedBlob staged);

        Stream OpenStaged(StagedBlob staged);

        Stream? OpenRead(string sha256);

        bool Exists(string sha256);

        void Delete(string sha256);

        IEnumerable<StoredBlob> EnumerateBlobs();

        IEnumerable<ScratchFile> EnumerateScratch();
    }
}
