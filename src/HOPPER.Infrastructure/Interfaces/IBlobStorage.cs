namespace HOPPER.Infrastructure.Interfaces
{
    public interface IBlobStorage
    {
        Task<(string Sha256, long Size)> SaveAsync(Stream content, CancellationToken cancellationToken = default);

        Stream? OpenRead(string sha256);

        bool Exists(string sha256);

        void Delete(string sha256);
    }
}
