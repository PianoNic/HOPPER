using System.IO.Compression;
using System.Text;
using HOPPER.Application.Command.Mods;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Tests.Application
{
    /// <summary>
    /// Multi-upload replaced both the single-file endpoint and the FTP drop the design once
    /// considered. What matters is that a batch is not all-or-nothing: an admin dragging forty jars
    /// where one is a duplicate must end up with thirty-nine stored and one explained, not with a
    /// rejected request and no way to tell which file offended.
    /// </summary>
    public class UploadModsCommandHandlerTests
    {
        private static readonly Guid ServerId = Guid.NewGuid();

        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hopper-upload-" + Guid.NewGuid().ToString("N"));
            public TempDir() => Directory.CreateDirectory(Path);
            public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
        }

        private sealed class StubUser(string? name) : ICurrentUserService
        {
            public string? Name { get; } = name;
        }

        private static HopperDbContext NewDb() =>
            new(new DbContextOptionsBuilder<HopperDbContext>()
                .UseInMemoryDatabase($"hopper-{Guid.NewGuid():N}")
                .Options);

        private static FileSystemBlobStorage StorageIn(string root) =>
            new(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Blobs:Directory"] = root })
                .Build());

        private static Stream Jar(string marker) => new MemoryStream(Encoding.UTF8.GetBytes($"PK jar {marker}"));

        private static Stream ZipOf(params (string Name, string Marker)[] entries)
        {
            var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, marker) in entries)
                {
                    using var stream = archive.CreateEntry(name).Open();
                    stream.Write(Encoding.UTF8.GetBytes($"PK jar {marker}"));
                }
            }

            buffer.Position = 0;
            return buffer;
        }

        [Test]
        public async Task Handle_SeveralJars_StoresThemAll()
        {
            using var dir = new TempDir();
            await using var db = NewDb();
            var handler = new UploadModsCommandHandler(db, StorageIn(dir.Path), new StubUser("alex"));

            var result = await handler.Handle(new UploadModsCommand(ServerId,
            [
                new UploadFile("jei.jar", Jar("jei")),
                new UploadFile("rei.jar", Jar("rei")),
                new UploadFile("journeymap.jar", Jar("jm")),
            ]), CancellationToken.None);

            await Assert.That(result.Uploaded).Count().IsEqualTo(3);
            await Assert.That(result.Failed).IsEmpty();
            await Assert.That(await db.Mods.CountAsync()).IsEqualTo(3);
            await Assert.That((await db.Mods.FirstAsync()).UploadedBy).IsEqualTo("alex");
        }

        [Test]
        public async Task Handle_Zip_IsExpandedIntoTheJarsItHolds()
        {
            using var dir = new TempDir();
            await using var db = NewDb();
            var handler = new UploadModsCommandHandler(db, StorageIn(dir.Path), new StubUser(null));

            var result = await handler.Handle(new UploadModsCommand(ServerId,
            [
                new UploadFile("mods.zip", ZipOf(("jei.jar", "jei"), ("rei.jar", "rei"))),
            ]), CancellationToken.None);

            await Assert.That(result.Uploaded.Select(m => m.FileName).Order().ToList())
                .IsEquivalentTo(new[] { "jei.jar", "rei.jar" });
        }

        [Test]
        public async Task Handle_ZipWithNestedFolders_TakesTheBasenameOnly()
        {
            // A client puts everything flat in hopper/, so the folder someone happened to zip a jar
            // from is not information it can use - and a path would fail the filename validator.
            using var dir = new TempDir();
            await using var db = NewDb();
            var handler = new UploadModsCommandHandler(db, StorageIn(dir.Path), new StubUser(null));

            var result = await handler.Handle(new UploadModsCommand(ServerId,
            [
                new UploadFile("pack.zip", ZipOf(("mods/client/jei.jar", "jei"))),
            ]), CancellationToken.None);

            await Assert.That(result.Uploaded.Single().FileName).IsEqualTo("jei.jar");
        }

        [Test]
        public async Task Handle_ZipEntriesThatAreNotJars_AreIgnored()
        {
            using var dir = new TempDir();
            await using var db = NewDb();
            var handler = new UploadModsCommandHandler(db, StorageIn(dir.Path), new StubUser(null));

            var result = await handler.Handle(new UploadModsCommand(ServerId,
            [
                new UploadFile("pack.zip", ZipOf(("jei.jar", "jei"), ("config/options.txt", "cfg"), ("__MACOSX/._jei.jar", "junk"))),
            ]), CancellationToken.None);

            await Assert.That(result.Uploaded.Select(m => m.FileName).ToList()).IsEquivalentTo(new[] { "jei.jar" });
            await Assert.That(result.Failed).IsEmpty();
        }

        [Test]
        public async Task Handle_BadNameInTheMiddle_FailsOnlyThatFile()
        {
            using var dir = new TempDir();
            await using var db = NewDb();
            var handler = new UploadModsCommandHandler(db, StorageIn(dir.Path), new StubUser(null));

            var result = await handler.Handle(new UploadModsCommand(ServerId,
            [
                new UploadFile("jei.jar", Jar("jei")),
                new UploadFile("readme.txt", Jar("nope")),
                new UploadFile("rei.jar", Jar("rei")),
            ]), CancellationToken.None);

            await Assert.That(result.Uploaded.Select(m => m.FileName).Order().ToList())
                .IsEquivalentTo(new[] { "jei.jar", "rei.jar" });
            await Assert.That(result.Failed).Count().IsEqualTo(1);
            await Assert.That(result.Failed[0].FileName).IsEqualTo("readme.txt");
        }

        [Test]
        public async Task Handle_DuplicateFileName_LandsInFailedAndTheRestStillSucceed()
        {
            using var dir = new TempDir();
            await using var db = NewDb();
            var handler = new UploadModsCommandHandler(db, StorageIn(dir.Path), new StubUser(null));

            await handler.Handle(new UploadModsCommand(ServerId, [new UploadFile("jei.jar", Jar("first"))]), CancellationToken.None);

            var result = await handler.Handle(new UploadModsCommand(ServerId,
            [
                new UploadFile("jei.jar", Jar("second")),
                new UploadFile("rei.jar", Jar("rei")),
            ]), CancellationToken.None);

            await Assert.That(result.Uploaded.Select(m => m.FileName).ToList()).IsEquivalentTo(new[] { "rei.jar" });
            await Assert.That(result.Failed).Count().IsEqualTo(1);
            await Assert.That(result.Failed[0].FileName).IsEqualTo("jei.jar");
            // The original row is untouched: a duplicate upload never silently replaces a jar, because
            // that would hand every client a same-named file with a new hash and no trace of the swap.
            await Assert.That(await db.Mods.CountAsync(m => m.FileName == "jei.jar")).IsEqualTo(1);
        }

        [Test]
        public async Task Handle_SameFileNameOnAnotherServer_IsNotADuplicate()
        {
            using var dir = new TempDir();
            await using var db = NewDb();
            var handler = new UploadModsCommandHandler(db, StorageIn(dir.Path), new StubUser(null));
            var otherServer = Guid.NewGuid();

            await handler.Handle(new UploadModsCommand(ServerId, [new UploadFile("jei.jar", Jar("a"))]), CancellationToken.None);
            var result = await handler.Handle(new UploadModsCommand(otherServer, [new UploadFile("jei.jar", Jar("b"))]), CancellationToken.None);

            await Assert.That(result.Failed).IsEmpty();
            await Assert.That(await db.Mods.CountAsync(m => m.FileName == "jei.jar")).IsEqualTo(2);
        }

        [Test]
        public async Task Handle_ZipWithNoJars_IsReportedAgainstTheArchive()
        {
            using var dir = new TempDir();
            await using var db = NewDb();
            var handler = new UploadModsCommandHandler(db, StorageIn(dir.Path), new StubUser(null));

            var result = await handler.Handle(new UploadModsCommand(ServerId,
            [
                new UploadFile("empty.zip", ZipOf(("readme.txt", "x"))),
            ]), CancellationToken.None);

            await Assert.That(result.Uploaded).IsEmpty();
            await Assert.That(result.Failed.Single().FileName).IsEqualTo("empty.zip");
        }

        [Test]
        public async Task Handle_SomethingNamedZipThatIsNot_IsReportedRatherThanThrown()
        {
            using var dir = new TempDir();
            await using var db = NewDb();
            var handler = new UploadModsCommandHandler(db, StorageIn(dir.Path), new StubUser(null));

            var result = await handler.Handle(new UploadModsCommand(ServerId,
            [
                new UploadFile("broken.zip", new MemoryStream(Encoding.UTF8.GetBytes("not a zip at all"))),
            ]), CancellationToken.None);

            await Assert.That(result.Failed.Single().FileName).IsEqualTo("broken.zip");
        }

        [Test]
        public async Task Handle_SameBytesTwiceUnderTwoNames_StoresOneBlob()
        {
            // Content addressing again: two names for one jar is two rows and one file.
            using var dir = new TempDir();
            await using var db = NewDb();
            var handler = new UploadModsCommandHandler(db, StorageIn(dir.Path), new StubUser(null));

            var result = await handler.Handle(new UploadModsCommand(ServerId,
            [
                new UploadFile("jei.jar", Jar("identical")),
                new UploadFile("jei-copy.jar", Jar("identical")),
            ]), CancellationToken.None);

            await Assert.That(result.Uploaded).Count().IsEqualTo(2);
            await Assert.That(result.Uploaded.Select(m => m.Sha256).Distinct().Count()).IsEqualTo(1);
        }
    }
}
