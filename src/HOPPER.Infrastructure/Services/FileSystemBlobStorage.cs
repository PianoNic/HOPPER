using System.Buffers;
using System.Security.Cryptography;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Infrastructure.Services
{
    /// <summary>Stores jars under &lt;root&gt;/&lt;sha[0..2]&gt;/&lt;sha[2..4]&gt;/&lt;sha&gt; with no
    /// file extension. The two levels of 2-hex fan-out cap any one directory at a handful of
    /// entries, which is how git and OCI address content and keeps directory listings cheap.</summary>
    public class FileSystemBlobStorage : IBlobStorage
    {
        private const int CopyBufferSize = 81920;

        private static readonly SearchValues<char> HexChars = SearchValues.Create("0123456789abcdef");

        private readonly string _root;

        public FileSystemBlobStorage(IConfiguration configuration)
        {
            var configured = configuration["Blobs:Directory"];
            _root = string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(AppContext.BaseDirectory, "blobs")
                : configured;
        }

        public async Task<(string Sha256, long Size)> SaveAsync(Stream content, CancellationToken cancellationToken = default)
        {
            var tempDirectory = Path.Combine(_root, "tmp");
            Directory.CreateDirectory(tempDirectory);
            var tempPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.part");

            string sha;
            long size = 0;

            try
            {
                using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                await using (var temp = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, CopyBufferSize, useAsync: true))
                {
                    // Hash and write in one pass over a fixed buffer. A content mod can run to
                    // hundreds of megabytes, so the bytes must never be materialised in an array.
                    var buffer = ArrayPool<byte>.Shared.Rent(CopyBufferSize);
                    try
                    {
                        int read;
                        while ((read = await content.ReadAsync(buffer.AsMemory(0, CopyBufferSize), cancellationToken)) > 0)
                        {
                            hash.AppendData(buffer, 0, read);
                            await temp.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                            size += read;
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }

                    // ToHexStringLower, not ToHexString().ToLower(): the wire format specifies
                    // lowercase and this makes that the only representation we can produce.
                    sha = Convert.ToHexStringLower(hash.GetHashAndReset());
                }

                var finalPath = ResolvePath(_root, sha);
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

                if (File.Exists(finalPath))
                {
                    // Identical content is already stored. Content addressing makes this a no-op
                    // rather than a conflict, which is what makes re-uploading the same jar free.
                    File.Delete(tempPath);
                }
                else
                {
                    try
                    {
                        File.Move(tempPath, finalPath);
                    }
                    catch (IOException) when (File.Exists(finalPath))
                    {
                        // A concurrent upload of the same bytes won the race. Its file is byte-identical
                        // to ours by definition, so drop ours.
                        File.Delete(tempPath);
                    }
                }
            }
            catch
            {
                TryDeleteTemp(tempPath);
                throw;
            }

            return (sha, size);
        }

        public Stream? OpenRead(string sha256)
        {
            var path = ResolvePath(_root, sha256);
            return File.Exists(path) ? File.OpenRead(path) : null;
        }

        public bool Exists(string sha256) => File.Exists(ResolvePath(_root, sha256));

        public void Delete(string sha256)
        {
            try
            {
                File.Delete(ResolvePath(_root, sha256));
            }
            catch (IOException)
            {
                // Already gone, or held open by a download in flight. Either way the row is what
                // makes a blob reachable, and that is already committed as deleted.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        // The hash arrives from the URL, so it is untrusted until proven to be exactly 64 lowercase
        // hex characters. Anything else must never reach Path.Combine — that is the whole traversal
        // defence. Uppercase is rejected rather than normalised: a case-insensitive store would give
        // the same content two addresses.
        private static string ResolvePath(string root, string sha256)
        {
            if (sha256.Length != 64 || sha256.AsSpan().ContainsAnyExcept(HexChars))
                throw new ArgumentException($"Not a sha256: {sha256}");

            return Path.Combine(root, sha256[..2], sha256[2..4], sha256);
        }

        private static void TryDeleteTemp(string tempPath)
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (IOException)
            {
            }
        }
    }
}
