using HOPPER.Domain;
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
        public async Task Store_Content_ReturnsTheSha256AndByteCount()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);
            var bytes = Encoding.UTF8.GetBytes("pretend this is a forge mod");

            var (sha, size) = await storage.StoreAsync(new MemoryStream(bytes), TestLimits.MaxBytes);

            await Assert.That(sha).IsEqualTo(Sha256Of(bytes));
            await Assert.That(size).IsEqualTo((long)bytes.Length);
        }

        [Test]
        public async Task Store_Hash_IsSixtyFourLowercaseHexCharacters()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);

            var (sha, _) = await storage.StoreAsync(new MemoryStream(Encoding.UTF8.GetBytes("x")), TestLimits.MaxBytes);

            await Assert.That(sha).Length().IsEqualTo(64);
            await Assert.That(sha.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))).IsTrue();
        }

        [Test]
        public async Task Store_LargeContent_HashesTheWholeStreamNotJustTheFirstBuffer()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);
            var bytes = new byte[81920 * 3 + 977];
            Random.Shared.NextBytes(bytes);

            var (sha, size) = await storage.StoreAsync(new MemoryStream(bytes), TestLimits.MaxBytes);

            await Assert.That(sha).IsEqualTo(Sha256Of(bytes));
            await Assert.That(size).IsEqualTo((long)bytes.Length);
        }

        [Test]
        public async Task Store_Content_LandsAtItsContentAddress()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);
            var bytes = Encoding.UTF8.GetBytes("addressed by its bytes");

            var (sha, _) = await storage.StoreAsync(new MemoryStream(bytes), TestLimits.MaxBytes);

            var expected = Path.Combine(dir.Path, sha[..2], sha[2..4], sha);
            await Assert.That(File.Exists(expected)).IsTrue();
            await Assert.That(await File.ReadAllBytesAsync(expected)).IsEquivalentTo(bytes);
        }

        [Test]
        public async Task Store_SameContentTwice_StoresOneFile()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);
            var bytes = Encoding.UTF8.GetBytes("identical");

            var (first, _) = await storage.StoreAsync(new MemoryStream(bytes), TestLimits.MaxBytes);
            var (second, _) = await storage.StoreAsync(new MemoryStream(bytes), TestLimits.MaxBytes);

            await Assert.That(second).IsEqualTo(first);

            var scratch = Path.Combine(dir.Path, "tmp") + Path.DirectorySeparatorChar;
            var stored = Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories)
                .Where(p => !p.StartsWith(scratch, StringComparison.Ordinal))
                .ToList();
            await Assert.That(stored).Count().IsEqualTo(1);
        }

        [Test]
        public async Task Store_Failure_LeavesNoPartialFileBehind()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);

            await Assert.That(async () => await storage.StoreAsync(new ThrowingStream(), TestLimits.MaxBytes))
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
            var (sha, _) = await storage.StoreAsync(new MemoryStream(bytes), TestLimits.MaxBytes);

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
        public async Task OpenRead_MalformedHash_IsAFaultAndNotARuleViolation()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);

            // The controller rejects a malformed hash before storage sees one, so reaching this guard
            // is a bug. Staying outside RuleViolationException is what keeps it a 500 rather than a
            // 400 dressed up with an internal message.
            var thrown = await Assert.That(() => storage.OpenRead("nope")).Throws<ArgumentException>();

            await Assert.That(thrown).IsNotAssignableTo<RuleViolationException>();
        }

        [Test]
        public async Task OpenRead_UppercaseHash_IsRejectedRatherThanFolded()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);
            var (sha, _) = await storage.StoreAsync(new MemoryStream(Encoding.UTF8.GetBytes("x")), TestLimits.MaxBytes);

            await Assert.That(() => storage.OpenRead(sha.ToUpperInvariant())).Throws<ArgumentException>();
        }

        [Test]
        public async Task Delete_StoredBlob_RemovesIt()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);
            var (sha, _) = await storage.StoreAsync(new MemoryStream(Encoding.UTF8.GetBytes("delete me")), TestLimits.MaxBytes);

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

        [Test]
        public async Task Store_StreamLongerThanMaxBytes_Throws()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);

            await Assert.That(async () => await storage.StoreAsync(new MemoryStream(new byte[101]), 100))
                .Throws<ContentTooLargeException>();
        }

        [Test]
        public async Task Store_StreamExactlyMaxBytes_IsStored()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);

            var (sha, size) = await storage.StoreAsync(new MemoryStream(new byte[100]), 100);

            await Assert.That(size).IsEqualTo(100L);
            await Assert.That(storage.Exists(sha)).IsTrue();
        }

        [Test]
        public async Task Store_StreamLongerThanMaxBytes_LeavesNoPartFileBehind()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);

            await Assert.That(async () => await storage.StoreAsync(new MemoryStream(new byte[101]), 100))
                .Throws<ContentTooLargeException>();

            await Assert.That(Directory.GetFiles(dir.Path, "*.part", SearchOption.AllDirectories)).IsEmpty();
            await Assert.That(Stored(dir.Path)).IsEmpty();
        }

        [Test]
        public async Task StageAsync_ThenDiscard_PublishesNothing()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);

            var staged = await storage.StageAsync(new MemoryStream(Encoding.UTF8.GetBytes("never committed")), TestLimits.MaxBytes);

            await Assert.That(storage.Exists(staged.Sha256)).IsFalse();

            storage.Discard(staged);

            await Assert.That(storage.Exists(staged.Sha256)).IsFalse();
            await Assert.That(File.Exists(staged.TempPath)).IsFalse();
            await Assert.That(Stored(dir.Path)).IsEmpty();
        }

        [Test]
        public async Task StageAsync_ThenPromote_StoresExactlyOneFile()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);
            var bytes = Encoding.UTF8.GetBytes("committed");

            var staged = await storage.StageAsync(new MemoryStream(bytes), TestLimits.MaxBytes);
            storage.Promote(staged);
            storage.Discard(staged);

            await Assert.That(staged.Sha256).IsEqualTo(Sha256Of(bytes));
            await Assert.That(storage.Exists(staged.Sha256)).IsTrue();
            await Assert.That(Stored(dir.Path)).Count().IsEqualTo(1);
            await Assert.That(File.Exists(staged.TempPath)).IsFalse();
        }

        [Test]
        public async Task Promote_WhenTheBlobAlreadyExists_KeepsTheExistingFileAndRemovesTheTemp()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);
            var bytes = Encoding.UTF8.GetBytes("identical bytes");

            var first = await storage.StageAsync(new MemoryStream(bytes), TestLimits.MaxBytes);
            var second = await storage.StageAsync(new MemoryStream(bytes), TestLimits.MaxBytes);

            storage.Promote(first);
            storage.Promote(second);

            await Assert.That(File.Exists(second.TempPath)).IsFalse();
            await Assert.That(Stored(dir.Path)).Count().IsEqualTo(1);
            await Assert.That(await File.ReadAllBytesAsync(Stored(dir.Path).Single())).IsEquivalentTo(bytes);
        }

        [Test]
        public async Task OpenStaged_BeforePromote_ReturnsTheBytesThatWereStaged()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);
            var bytes = Encoding.UTF8.GetBytes("readable while staged");

            var staged = await storage.StageAsync(new MemoryStream(bytes), TestLimits.MaxBytes);

            using (var stream = storage.OpenStaged(staged))
            using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer);
                await Assert.That(buffer.ToArray()).IsEquivalentTo(bytes);
            }

            storage.Discard(staged);
        }

        [Test]
        public async Task EnumerateBlobs_ReturnsStoredBlobsAndNothingFromTheScratchDirectories()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);

            var (sha, _) = await storage.StoreAsync(new MemoryStream(Encoding.UTF8.GetBytes("a blob")), TestLimits.MaxBytes);

            Directory.CreateDirectory(Path.Combine(dir.Path, "imports"));
            await File.WriteAllTextAsync(Path.Combine(dir.Path, "imports", $"{Guid.NewGuid():N}.pack"), "pack");
            Directory.CreateDirectory(Path.Combine(dir.Path, "exports"));
            await File.WriteAllTextAsync(Path.Combine(dir.Path, "exports", $"{Guid.NewGuid():N}.tmp"), "export");
            Directory.CreateDirectory(Path.Combine(dir.Path, "tmp"));
            await File.WriteAllTextAsync(Path.Combine(dir.Path, "tmp", $"{Guid.NewGuid():N}.part"), "part");

            var found = storage.EnumerateBlobs().ToList();

            await Assert.That(found.Select(b => b.Sha256).ToList()).IsEquivalentTo(new[] { sha });
        }

        [Test]
        public async Task EnumerateScratch_ReturnsTempPartsAndExportScratchOnly()
        {
            using var dir = new TempDir();
            var storage = StorageIn(dir.Path);

            await storage.StoreAsync(new MemoryStream(Encoding.UTF8.GetBytes("a blob")), TestLimits.MaxBytes);

            Directory.CreateDirectory(Path.Combine(dir.Path, "tmp"));
            var part = Path.Combine(dir.Path, "tmp", $"{Guid.NewGuid():N}.part");
            await File.WriteAllTextAsync(part, "part");

            Directory.CreateDirectory(Path.Combine(dir.Path, "exports"));
            var export = Path.Combine(dir.Path, "exports", $"{Guid.NewGuid():N}.tmp");
            await File.WriteAllTextAsync(export, "export");

            Directory.CreateDirectory(Path.Combine(dir.Path, "imports"));
            await File.WriteAllTextAsync(Path.Combine(dir.Path, "imports", $"{Guid.NewGuid():N}.pack"), "pack");

            var found = storage.EnumerateScratch().Select(s => s.Path).Order().ToList();

            await Assert.That(found).IsEquivalentTo(new[] { part, export }.Order().ToList());
        }

        private static List<string> Stored(string root)
        {
            var scratch = new[] { "tmp", "imports", "exports" }
                .Select(d => Path.Combine(root, d) + Path.DirectorySeparatorChar)
                .ToList();

            return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(p => !scratch.Any(s => p.StartsWith(s, StringComparison.Ordinal)))
                .ToList();
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
