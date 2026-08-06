using Microsoft.Extensions.Configuration;

namespace HOPPER.Application.Imports
{
    public interface IImportStaging
    {
        string PackPath(Guid importId);

        string WorkDirectory(Guid importId);

        Task<long> StageAsync(Guid importId, Stream content, long maxBytes, CancellationToken cancellationToken);

        void Cleanup(Guid importId);
    }

    public class ImportStaging(IConfiguration configuration) : IImportStaging
    {
        private const int CopyBufferSize = 81920;

        private readonly string _root = Path.Combine(
            configuration["Blobs:Directory"] is { Length: > 0 } configured
                ? configured
                : Path.Combine(AppContext.BaseDirectory, "blobs"),
            "imports");

        public string PackPath(Guid importId) => Path.Combine(_root, $"{importId:N}.pack");

        public string WorkDirectory(Guid importId) => Path.Combine(_root, importId.ToString("N"));

        public async Task<long> StageAsync(Guid importId, Stream content, long maxBytes, CancellationToken cancellationToken)
        {
            Directory.CreateDirectory(_root);
            var path = PackPath(importId);

            long written = 0;

            try
            {
                await using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true))
                {
                    var buffer = new byte[CopyBufferSize];
                    int read;
                    while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        written += read;
                        if (written > maxBytes)
                        {
                            throw new ArgumentException(
                                $"The pack is larger than the {maxBytes} byte limit. Raise Hopper:MaxImportBytes to accept it.");
                        }

                        await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    }
                }
            }
            catch
            {
                TryDelete(path);
                throw;
            }

            return written;
        }

        public void Cleanup(Guid importId)
        {
            TryDelete(PackPath(importId));

            try
            {
                if (Directory.Exists(WorkDirectory(importId)))
                    Directory.Delete(WorkDirectory(importId), recursive: true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
