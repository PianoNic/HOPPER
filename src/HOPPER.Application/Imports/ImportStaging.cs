using Microsoft.Extensions.Configuration;

namespace HOPPER.Application.Imports
{
    /// <summary>Where a pack's bytes live between the request that accepted it and the worker that
    /// reads it. Not the blob store: a pack is not content-addressed, is not a mod, and is deleted the
    /// moment the import finishes. It sits under the same root only so a deployment that gave HOPPER
    /// one big volume does not need a second one.</summary>
    public interface IImportStaging
    {
        /// <summary>The staged archive for an import. Exists between StageAsync and Cleanup.</summary>
        string PackPath(Guid importId);

        /// <summary>Scratch directory for one import's in-flight downloads.</summary>
        string WorkDirectory(Guid importId);

        /// <summary>Streams the upload to disk, aborting past <paramref name="maxBytes"/>. Never
        /// buffers: a modpack export runs to tens of gigabytes when someone exports their saves with
        /// it, and Content-Length is a claim, not a guarantee.</summary>
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
                            // Counted as it arrives rather than trusted from a header, and thrown as an
                            // ArgumentException so the API answers 400 rather than 500.
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
