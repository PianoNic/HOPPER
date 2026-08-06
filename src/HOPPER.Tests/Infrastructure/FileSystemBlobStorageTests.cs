using System.Security.Cryptography;
using System.Text;
using HOPPER.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Tests.Infrastructure
{
    public class FileSystemBlobStorageTests
    {
        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hopper-test-" + Guid.NewGuid().ToString("N"));
            public TempDir() => Directory.CreateDirectory(Path);
            public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
        }

        private static FileSystemBlobStorage StorageIn(string root) =>
            new(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Blobs:Directory"] = root })
                .Build());

        private static string Sha256Of(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

        [Test]
        public async Task SaveAsync_Content_ReturnsTheSha256AndByteCount()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);
            var bytes = Encoding.UTF8.GetBytes("pretend this is a forge mod");

            var (sha, size) = await storage.SaveAsync(new MemoryStream(bytes));

            await Assert.That(sha).IsEqualTo(Sha256Of(bytes));
            await Assert.That(size).IsEqualTo((long)bytes.Length);
        }

        [Test]
        public async Task SaveAsync_Hash_IsSixtyFourLowercaseHexCharacters()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);

            var (sha, _) = await storage.SaveAsync(new MemoryStream(Encoding.UTF8.GetBytes("x")));

            await Assert.That(sha).Length().IsEqualTo(64);
            await Assert.That(sha.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))).IsTrue();
        }

        [Test]
        public async Task SaveAsync_LargeContent_HashesTheWholeStreamNotJustTheFirstBuffer()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);
            var bytes = new byte[81920 * 3 + 977];
            Random.Shared.NextBytes(bytes);

            var (sha, size) = await storage.SaveAsync(new MemoryStream(bytes));

            await Assert.That(sha).IsEqualTo(Sha256Of(bytes));
            await Assert.That(size).IsEqualTo((long)bytes.Length);
        }

        [Test]
        public async Task SaveAsync_Content_LandsAtItsContentAddress()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);
            var bytes = Encoding.UTF8.GetBytes("addressed by its bytes");

            var (sha, _) = await storage.SaveAsync(new MemoryStream(bytes));

            var expected = Path.Combine(dir.Path, sha[..2], sha[2..4], sha);
            await Assert.That(File.Exists(expected)).IsTrue();
            await Assert.That(await File.ReadAllBytesAsync(expected)).IsEquivalentTo(bytes);
        }

        [Test]
        public async Task SaveAsync_SameContentTwice_StoresOneFile()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);
            var bytes = Encoding.UTF8.GetBytes("identical");

            var (first, _) = await storage.SaveAsync(new MemoryStream(bytes));
            var (second, _) = await storage.SaveAsync(new MemoryStream(bytes));

            await Assert.That(second).IsEqualTo(first);

            var scratch = Path.Combine(dir.Path, "tmp") + Path.DirectorySeparatorChar;
            var stored = Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories)
                .Where(p => !p.StartsWith(scratch, StringComparison.Ordinal))
                .ToList();
            await Assert.That(stored).Count().IsEqualTo(1);
        }

        [Test]
        public async Task SaveAsync_Failure_LeavesNoPartialFileBehind()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);

            await Assert.That(async () => await storage.SaveAsync(new ThrowingStream()))
                .Throws<IOException>();

            var leftovers = Directory.GetFiles(dir.Path, "*.part", SearchOption.AllDirectories);
            await Assert.That(leftovers).IsEmpty();
        }

        [Test]
        public async Task OpenRead_StoredBlob_ReturnsTheOriginalBytes()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);
            var bytes = Encoding.UTF8.GetBytes("streamed back out");
            var (sha, _) = await storage.SaveAsync(new MemoryStream(bytes));

            await using var stream = storage.OpenRead(sha);
            await Assert.That(stream).IsNotNull();
            using var buffer = new MemoryStream();
            await stream!.CopyToAsync(buffer);

            await Assert.That(buffer.ToArray()).IsEquivalentTo(bytes);
            await Assert.That(storage.Exists(sha)).IsTrue();
        }

        [Test]
        public async Task OpenRead_UnknownButWellFormedHash_ReturnsNull()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);

            await Assert.That(storage.OpenRead(new string('f', 64))).IsNull();
            await Assert.That(storage.Exists(new string('f', 64))).IsFalse();
        }

        [Test]
        [Arguments("../../../etc/passwd")]
        [Arguments("..")]
        [Arguments("")]
        [Arguments("abc")]
        public async Task OpenRead_HashThatIsNotSixtyFourHexCharacters_IsRejected(string sha)
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);

            await Assert.That(() => storage.OpenRead(sha)).Throws<ArgumentException>();
        }

        [Test]
        public async Task OpenRead_UppercaseHash_IsRejectedRatherThanFolded()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);
            var (sha, _) = await storage.SaveAsync(new MemoryStream(Encoding.UTF8.GetBytes("x")));

            await Assert.That(() => storage.OpenRead(sha.ToUpperInvariant())).Throws<ArgumentException>();
        }

        [Test]
        public async Task Delete_StoredBlob_RemovesIt()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);
            var (sha, _) = await storage.SaveAsync(new MemoryStream(Encoding.UTF8.GetBytes("delete me")));

            storage.Delete(sha);

            await Assert.That(storage.Exists(sha)).IsFalse();
        }

        [Test]
        public async Task Delete_BlobThatIsAlreadyGone_IsNotAnError()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);

            storage.Delete(new string('e', 64));

            await Assert.That(storage.Exists(new string('e', 64))).IsFalse();
        }

        private sealed class ThrowingStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override int Read(byte[] buffer, int offset, int count) => throw new IOException("boom");
            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
                => throw new IOException("boom");
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
