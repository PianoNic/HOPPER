using System.IO.Compression;
using System.Text;
using HOPPER.Application.ModMetadata;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Tests.ModMetadata
{
    public class ModJarReaderTests
    {
        private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

        private sealed class TempDir : IDisposable
        {
            public string Path { get; } =
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hopper-jarmeta-" + Guid.NewGuid().ToString("N"));

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

        private static MemoryStream FabricJar()
        {
            var buffer = new MemoryStream();

            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                using (var json = archive.CreateEntry("fabric.mod.json").Open())
                {
                    json.Write(Encoding.UTF8.GetBytes(
                        "{\"id\":\"sodium\",\"environment\":\"client\",\"icon\":\"assets/icon.png\"}"));
                }

                using var icon = archive.CreateEntry("assets/icon.png").Open();
                icon.Write(Png);
            }

            buffer.Position = 0;
            return buffer;
        }

        private static MemoryStream NotAJar()
        {
            var buffer = new MemoryStream(Encoding.UTF8.GetBytes("this is not a zip file at all"));
            buffer.Position = 0;
            return buffer;
        }

        [Test]
        public async Task ReadsSideIdsAndIconFromOneJar()
        {
            using var dir = new TempDir();
            var blobs = StorageIn(dir.Path);

            using var jar = FabricJar();
            var staged = await blobs.StageAsync(jar, TestLimits.MaxBytes, CancellationToken.None);

            var metadata = await ModJarReader.FromStagedAsync(blobs, staged, CancellationToken.None);

            await Assert.That(metadata.Side).IsEqualTo(ModSide.ClientOnly);
            await Assert.That(metadata.ModIds).IsEquivalentTo(new[] { "sodium" });
            await Assert.That(metadata.IconSha256).IsNotNull();
            await Assert.That(blobs.Exists(metadata.IconSha256!)).IsTrue();
        }

        [Test]
        public async Task AgreesWithTheThreeReadersReachedSeparately()
        {
            // The whole point of merging them: one archive open has to answer exactly what three did.
            using var dir = new TempDir();
            var blobs = StorageIn(dir.Path);

            using var jar = FabricJar();
            var staged = await blobs.StageAsync(jar, TestLimits.MaxBytes, CancellationToken.None);

            var metadata = await ModJarReader.FromStagedAsync(blobs, staged, CancellationToken.None);

            await Assert.That(metadata.Side).IsEqualTo(ModSideReader.FromStaged(blobs, staged));
            await Assert.That(metadata.ModIds).IsEquivalentTo(ModIdReader.FromStaged(blobs, staged)!);
        }

        [Test]
        public async Task AJarThatWillNotOpenReportsNoIdsRatherThanUnknownIds()
        {
            // Not the same as never having looked: null is what the backfill treats as "come back to it",
            // so a file that is simply not a zip has to come back as an empty list instead.
            using var dir = new TempDir();
            var blobs = StorageIn(dir.Path);

            using var junk = NotAJar();
            var staged = await blobs.StageAsync(junk, TestLimits.MaxBytes, CancellationToken.None);

            var metadata = await ModJarReader.FromStagedAsync(blobs, staged, CancellationToken.None);

            await Assert.That(metadata.ModIds).IsNotNull();
            await Assert.That(metadata.ModIds!).IsEmpty();
            await Assert.That(metadata.Side).IsEqualTo(ModSide.Both);
            await Assert.That(metadata.IconSha256).IsNull();
        }

        [Test]
        public async Task AJarWithNoMetadataAtAllIsBothWithNoIdsAndNoIcon()
        {
            using var dir = new TempDir();
            var blobs = StorageIn(dir.Path);

            var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var stray = archive.CreateEntry("net/example/Thing.class").Open();
                stray.Write(Encoding.UTF8.GetBytes("nothing useful"));
            }

            buffer.Position = 0;
            var staged = await blobs.StageAsync(buffer, TestLimits.MaxBytes, CancellationToken.None);

            var metadata = await ModJarReader.FromStagedAsync(blobs, staged, CancellationToken.None);

            await Assert.That(metadata.Side).IsEqualTo(ModSide.Both);
            await Assert.That(metadata.ModIds!).IsEmpty();
            await Assert.That(metadata.IconSha256).IsNull();
        }
    }
}
