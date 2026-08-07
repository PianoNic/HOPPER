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
            _root = BlobPaths.Root(configuration);
        }

        public async Task<StagedBlob> StageAsync(Stream content, long maxBytes, CancellationToken cancellationToken = default)
        {
            var tempDirectory = Path.Combine(_root, "tmp");
            Directory.CreateDirectory(tempDirectory);
            var tempPath = Path.Combine(tempDirectory, $"{Guid.NewGuid():N}.part");

            var limited = new LimitedStream(content, maxBytes, "This file");

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
                        while ((read = await limited.ReadAsync(buffer.AsMemory(0, CopyBufferSize), cancellationToken)) > 0)
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
            }
            catch
            {
                TryDeleteTemp(tempPath);
                throw;
            }

            return new StagedBlob(sha, size, tempPath);
        }

        public void Promote(StagedBlob staged)
        {
            var finalPath = ResolvePath(_root, staged.Sha256);
            Directory.CreateDirectory(Path.GetDirectoryName(finalPath)!);

            if (File.Exists(finalPath))
            {
                TryDeleteTemp(staged.TempPath);
                return;
            }

            try
            {
                File.Move(staged.TempPath, finalPath);
            }
            catch (IOException) when (File.Exists(finalPath))
            {
                TryDeleteTemp(staged.TempPath);
            }
        }

        public void Discard(StagedBlob staged) => TryDeleteTemp(staged.TempPath);

        public Stream OpenStaged(StagedBlob staged) =>
            new FileStream(staged.TempPath, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBufferSize);

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

        public IEnumerable<StoredBlob> EnumerateBlobs()
        {
            if (!Directory.Exists(_root))
                yield break;

            foreach (var first in Directory.EnumerateDirectories(_root))
            {
                if (!IsHex(Path.GetFileName(first), 2))
                    continue;

                foreach (var second in Directory.EnumerateDirectories(first))
                {
                    if (!IsHex(Path.GetFileName(second), 2))
                        continue;

                    foreach (var file in Directory.EnumerateFiles(second))
                    {
                        var name = Path.GetFileName(file);
                        if (!IsHex(name, 64))
                            continue;

                        yield return new StoredBlob(name, File.GetLastWriteTimeUtc(file));
                    }
                }
            }
        }

        public IEnumerable<ScratchFile> EnumerateScratch()
        {
            foreach (var file in ScratchIn(Path.Combine(_root, "tmp"), "*.part"))
                yield return file;

            foreach (var file in ScratchIn(Path.Combine(_root, "exports"), "*.tmp"))
                yield return file;
        }

        private static IEnumerable<ScratchFile> ScratchIn(string directory, string pattern)
        {
            if (!Directory.Exists(directory))
                yield break;

            foreach (var file in Directory.EnumerateFiles(directory, pattern))
                yield return new ScratchFile(file, File.GetLastWriteTimeUtc(file));
        }

        private static bool IsHex(string value, int length) =>
            value.Length == length && !value.AsSpan().ContainsAnyExcept(HexChars);

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
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
