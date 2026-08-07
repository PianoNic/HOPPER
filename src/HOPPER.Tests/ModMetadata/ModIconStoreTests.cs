using System.IO.Compression;
using System.Text;
using HOPPER.Application.ModMetadata;
using HOPPER.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Tests.ModMetadata
{
    public class ModIconStoreTests
    {
        private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 7, 7, 7];

        private const string RealToml =
            "modLoader = \"javafml\"\r\nloaderVersion = \"[46,)\"\r\n\r\n[[mods]]\r\nmodId = \"jade\"\r\nlogoFile = \"icon.png\"\r\n";

        private sealed class TempDir : IDisposable
        {
            public string Path { get; } =
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hopper-icons-" + Guid.NewGuid().ToString("N"));

            public TempDir() => Directory.CreateDirectory(Path);

            public void Dispose()
            {
                try { Directory.Delete(Path, recursive: true); } catch { }
            }
        }

        private static FileSystemBlobStorage StorageIn(string root) =>
            new(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Blobs:Directory"] = root })
                .Build());

        private static MemoryStream Jar()
        {
            var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var toml = archive.CreateEntry("META-INF/mods.toml").Open())
                    toml.Write(Encoding.UTF8.GetBytes(RealToml));

                using var icon = archive.CreateEntry("icon.png").Open();
                icon.Write(Png);
            }

            buffer.Position = 0;
            return buffer;
        }

        [Test]
        public async Task FromJarAsync_StoresTheIconOfAJarAlreadyInTheStore()
        {
            using var dir = new TempDir();
            var blobs = StorageIn(dir.Path);

            using var jar = Jar();
            var staged = await blobs.StageAsync(jar, 10_000_000, CancellationToken.None);
            blobs.Promote(staged);

            var icon = await ModIconStore.FromJarAsync(blobs, staged.Sha256, CancellationToken.None);

            await Assert.That(icon).IsNotNull();
            await Assert.That(blobs.Exists(icon!)).IsTrue();

            using var stored = blobs.OpenRead(icon!);
            using var read = new MemoryStream();
            await stored!.CopyToAsync(read);
            await Assert.That(read.ToArray()).IsEquivalentTo(Png);
        }

        [Test]
        public async Task FromStagedJarAsync_StoresTheIconBeforeTheJarIsPromoted()
        {
            using var dir = new TempDir();
            var blobs = StorageIn(dir.Path);

            using var jar = Jar();
            var staged = await blobs.StageAsync(jar, 10_000_000, CancellationToken.None);

            var icon = await ModIconStore.FromStagedJarAsync(blobs, staged, CancellationToken.None);

            await Assert.That(icon).IsNotNull();
            await Assert.That(blobs.Exists(icon!)).IsTrue();
        }

        [Test]
        public async Task FromJarAsync_GivesTheSameShaForTheSameIcon()
        {
            using var dir = new TempDir();
            var blobs = StorageIn(dir.Path);

            using var jar = Jar();
            var staged = await blobs.StageAsync(jar, 10_000_000, CancellationToken.None);
            blobs.Promote(staged);

            var first = await ModIconStore.FromJarAsync(blobs, staged.Sha256, CancellationToken.None);
            var second = await ModIconStore.FromJarAsync(blobs, staged.Sha256, CancellationToken.None);

            await Assert.That(first).IsEqualTo(second);
        }

        [Test]
        public async Task FromJarAsync_IsNullForAShaTheStoreDoesNotHave()
        {
            using var dir = new TempDir();
            var blobs = StorageIn(dir.Path);

            var icon = await ModIconStore.FromJarAsync(blobs, new string('a', 64), CancellationToken.None);

            await Assert.That(icon).IsNull();
        }
    }
}
