using System.Buffers;
using System.Security.Cryptography;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Infrastructure.Services
{
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

                    sha = Convert.ToHexStringLower(hash.GetHashAndReset());
                }

                var finalPath = ResolvePath(_root, sha);
                Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

                if (File.Exists(finalPath))
                {
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
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

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
