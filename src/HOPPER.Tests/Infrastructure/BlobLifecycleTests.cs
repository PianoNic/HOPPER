using System.Diagnostics;
using System.Text;
using HOPPER.Application;
using HOPPER.Application.Command.Mods;
using HOPPER.Domain;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Tests.Infrastructure
{
    public class BlobLifecycleTests
    {
        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hopper-lifecycle-" + Guid.NewGuid().ToString("N"));
            public TempDir() => Directory.CreateDirectory(Path);
            public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
        }

        private sealed class StubUser(string? name) : ICurrentUserService
        {
            public string? Name { get; } = name;
        }

        private static FileSystemBlobStorage StorageIn(string root) =>
            new(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Blobs:Directory"] = root })
                .Build());

        private static Stream Jar(string marker) => new MemoryStream(Encoding.UTF8.GetBytes($"PK jar {marker}"));

        private static async Task<Guid> SeedServerAsync(HopperDbContext db, string suffix)
        {
            var server = new Server
            {
                Name = $"Server {suffix}",
                Slug = $"server-{suffix}",
                Token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            };

            db.Servers.Add(server);
            await db.SaveChangesAsync();
            return server.Id;
        }

        [Test]
        public async Task DeleteMod_LastReference_RemovesTheBytesImmediately()
        {
            using var dir = new TempDir();
            await using var db = PostgresHarness.Context(await PostgresHarness.NewMigratedDatabaseAsync());
            var blobs = StorageIn(dir.Path);
            var serverId = await SeedServerAsync(db, "a");

            await new UploadModsCommandHandler(db, blobs, new StubUser("alex"), TestLimits.Config)
                .Handle(new UploadModsCommand(serverId, [new UploadFile("jei.jar", Jar("jei"))]), CancellationToken.None);

            var row = await db.Mods.SingleAsync();
            await Assert.That(blobs.Exists(row.Sha256)).IsTrue();

            await new DeleteModsCommandHandler(db, blobs)
                .Handle(new DeleteModsCommand(serverId, [row.Id]), CancellationToken.None);

            await Assert.That(blobs.Exists(row.Sha256)).IsFalse();
        }

        [Test]
        public async Task DeleteMod_WhenAnotherServerStillReferencesTheHash_KeepsTheBytes()
        {
            using var dir = new TempDir();
            await using var db = PostgresHarness.Context(await PostgresHarness.NewMigratedDatabaseAsync());
            var blobs = StorageIn(dir.Path);
            var a = await SeedServerAsync(db, "a");
            var b = await SeedServerAsync(db, "b");
            var handler = new UploadModsCommandHandler(db, blobs, new StubUser(null), TestLimits.Config);

            await handler.Handle(new UploadModsCommand(a, [new UploadFile("jei.jar", Jar("shared"))]), CancellationToken.None);
            await handler.Handle(new UploadModsCommand(b, [new UploadFile("jei.jar", Jar("shared"))]), CancellationToken.None);

            var onA = await db.Mods.SingleAsync(m => m.ServerId == a);

            await new DeleteModsCommandHandler(db, blobs)
                .Handle(new DeleteModsCommand(a, [onA.Id]), CancellationToken.None);

            await Assert.That(blobs.Exists(onA.Sha256)).IsTrue();
        }

        [Test]
        public async Task Store_WhenTheRowInsertFails_PublishesNoBlob()
        {
            using var dir = new TempDir();
            var connectionString = await PostgresHarness.NewMigratedDatabaseAsync();
            await using var db = PostgresHarness.Context(connectionString);
            var blobs = StorageIn(dir.Path);
            var serverId = await SeedServerAsync(db, "a");

            var racing = new RacingBlobs(blobs, async () =>
            {
                await using var other = PostgresHarness.Context(connectionString);
                other.Mods.Add(new Mod
                {
                    ServerId = serverId,
                    FileName = "jei.jar",
                    Sha256 = new string('a', 64),
                    Size = 1,
                });
                await other.SaveChangesAsync();
            });

            var result = await new UploadModsCommandHandler(db, racing, new StubUser(null), TestLimits.Config)
                .Handle(new UploadModsCommand(serverId, [new UploadFile("jei.jar", Jar("loser"))]), CancellationToken.None);

            await Assert.That(result.Failed).Count().IsEqualTo(1);
            await Assert.That(result.Uploaded).IsEmpty();
            await Assert.That(racing.LastStaged).IsNotNull();
            await Assert.That(blobs.Exists(racing.LastStaged!.Sha256)).IsFalse();
            await Assert.That(File.Exists(racing.LastStaged.TempPath)).IsFalse();
        }

        [Test]
        public async Task Store_RowInsertedInsideTheStoreWindow_StillImportsTheOtherFilesInTheBatch()
        {
            using var dir = new TempDir();
            var connectionString = await PostgresHarness.NewMigratedDatabaseAsync();
            await using var db = PostgresHarness.Context(connectionString);
            var blobs = StorageIn(dir.Path);
            var serverId = await SeedServerAsync(db, "a");

            var once = true;
            var racing = new RacingBlobs(blobs, async () =>
            {
                if (!once) return;
                once = false;

                await using var other = PostgresHarness.Context(connectionString);
                other.Mods.Add(new Mod
                {
                    ServerId = serverId,
                    FileName = "jei.jar",
                    Sha256 = new string('b', 64),
                    Size = 1,
                });
                await other.SaveChangesAsync();
            });

            var result = await new UploadModsCommandHandler(db, racing, new StubUser(null), TestLimits.Config)
                .Handle(new UploadModsCommand(serverId,
                [
                    new UploadFile("jei.jar", Jar("loser")),
                    new UploadFile("rei.jar", Jar("winner")),
                ]), CancellationToken.None);

            await Assert.That(result.Failed.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "jei.jar" });
            await Assert.That(result.Uploaded.Select(u => u.FileName).ToList()).IsEquivalentTo(new[] { "rei.jar" });
        }

        [Test]
        public async Task BlobLock_SecondHolderOfTheSameHash_WaitsForTheFirst()
        {
            var connectionString = await PostgresHarness.NewMigratedDatabaseAsync();
            var sha = new string('c', 64);

            await using var first = PostgresHarness.Context(connectionString);
            await using var second = PostgresHarness.Context(connectionString);

            var held = await BlobLock.HoldAsync(first, sha);

            var contender = Task.Run(async () =>
            {
                await using var hold = await BlobLock.HoldAsync(second, sha);
                return Stopwatch.GetTimestamp();
            });

            await Task.Delay(250);
            var released = Stopwatch.GetTimestamp();
            await held.DisposeAsync();

            var acquired = await contender;

            await Assert.That(acquired).IsGreaterThanOrEqualTo(released);
        }

        [Test]
        public async Task BlobLock_DifferentHashes_DoNotBlockEachOther()
        {
            var connectionString = await PostgresHarness.NewMigratedDatabaseAsync();

            await using var first = PostgresHarness.Context(connectionString);
            await using var second = PostgresHarness.Context(connectionString);

            await using var held = await BlobLock.HoldAsync(first, new string('d', 64));

            var other = BlobLock.HoldAsync(second, new string('e', 64));
            var finished = await Task.WhenAny(other, Task.Delay(TimeSpan.FromSeconds(5)));

            await Assert.That(finished).IsSameReferenceAs(other);

            await (await other).DisposeAsync();
        }

        [Test]
        public async Task Collect_BlobStillReferencedByAnotherServer_IsKept()
        {
            using var dir = new TempDir();
            await using var db = PostgresHarness.Context(await PostgresHarness.NewMigratedDatabaseAsync());
            var blobs = StorageIn(dir.Path);
            var a = await SeedServerAsync(db, "a");
            var b = await SeedServerAsync(db, "b");
            var handler = new UploadModsCommandHandler(db, blobs, new StubUser(null), TestLimits.Config);

            await handler.Handle(new UploadModsCommand(a, [new UploadFile("jei.jar", Jar("shared"))]), CancellationToken.None);
            await handler.Handle(new UploadModsCommand(b, [new UploadFile("jei.jar", Jar("shared"))]), CancellationToken.None);

            var sha = (await db.Mods.FirstAsync()).Sha256;

            var collected = await BlobCollector.CollectAsync(db, blobs, sha);

            await Assert.That(collected).IsFalse();
            await Assert.That(blobs.Exists(sha)).IsTrue();
        }

        private sealed class RacingBlobs(IBlobStorage inner, Func<Task> duringStage) : IBlobStorage
        {
            public StagedBlob? LastStaged { get; private set; }

            public async Task<StagedBlob> StageAsync(Stream content, long maxBytes, CancellationToken cancellationToken = default)
            {
                var staged = await inner.StageAsync(content, maxBytes, cancellationToken);
                LastStaged = staged;
                await duringStage();
                return staged;
            }

            public void Promote(StagedBlob staged) => inner.Promote(staged);

            public void Discard(StagedBlob staged) => inner.Discard(staged);

            public Stream OpenStaged(StagedBlob staged) => inner.OpenStaged(staged);

            public Stream? OpenRead(string sha256) => inner.OpenRead(sha256);

            public bool Exists(string sha256) => inner.Exists(sha256);

            public void Delete(string sha256) => inner.Delete(sha256);

            public IEnumerable<StoredBlob> EnumerateBlobs() => inner.EnumerateBlobs();

            public IEnumerable<ScratchFile> EnumerateScratch() => inner.EnumerateScratch();
        }
    }
}
