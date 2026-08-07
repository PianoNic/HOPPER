using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using HOPPER.Application;
using HOPPER.Application.Command.Modrinth;
using HOPPER.Domain;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using HOPPER.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Tests.Modrinth
{
    public class InstallModrinthBlobLockTests
    {
        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "hopper-modrinth-lock-" + Guid.NewGuid().ToString("N"));

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

        private static byte[] Jar(string marker) => Encoding.UTF8.GetBytes($"PK jar {marker}");

        private static string Sha256Of(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

        private static async Task<Guid> SeedServerAsync(HopperDbContext db, string suffix)
        {
            var server = new Server
            {
                Name = $"Server {suffix}",
                Slug = $"server-{suffix}-{Guid.NewGuid().ToString("N")[..8]}",
                Token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                MinecraftVersion = "1.20.1",
                Loader = HOPPER.Domain.Enums.ModLoader.Forge,
                LoaderVersion = "47.4.10",
            };

            db.Servers.Add(server);
            await db.SaveChangesAsync();
            return server.Id;
        }

        [Test]
        public async Task Install_WhileTheSameHashIsLockedElsewhere_WaitsForThatHolder()
        {
            using var dir = new TempDir();
            var connectionString = await PostgresHarness.NewMigratedDatabaseAsync();

            await using var db = PostgresHarness.Context(connectionString);
            await using var other = PostgresHarness.Context(connectionString);

            var blobs = StorageIn(dir.Path);
            var serverId = await SeedServerAsync(db, "a");

            var bytes = Jar("locked");
            var client = new FakeModrinthClient();
            client.AddDownloadableMod("PA", "v-a", "A", "a.jar", bytes);

            var held = await BlobLock.HoldAsync(other, Sha256Of(bytes));

            var install = Task.Run(async () =>
            {
                await using var scoped = PostgresHarness.Context(connectionString);
                var result = await new InstallModrinthModsCommandHandler(
                        scoped, blobs, client, new StubUser("alex"), TestLimits.Config)
                    .Handle(new InstallModrinthModsCommand(serverId, [new ModrinthInstallItem("v-a", false)]),
                        CancellationToken.None);

                return (Finished: Stopwatch.GetTimestamp(), Result: result);
            });

            await Task.Delay(750);

            await Assert.That(install.IsCompleted).IsFalse();

            var released = Stopwatch.GetTimestamp();
            await held.DisposeAsync();

            var (finished, installed) = await install;

            await Assert.That(finished).IsGreaterThanOrEqualTo(released);
            await Assert.That(installed.Installed).Count().IsEqualTo(1);
            await Assert.That(blobs.Exists(Sha256Of(bytes))).IsTrue();
        }

        [Test]
        public async Task Install_CollectOfTheSameHashBetweenTheDownloadAndTheRow_StillLeavesTheBytesOnDisk()
        {
            using var dir = new TempDir();
            var connectionString = await PostgresHarness.NewMigratedDatabaseAsync();

            await using var db = PostgresHarness.Context(connectionString);

            var blobs = StorageIn(dir.Path);
            var a = await SeedServerAsync(db, "a");
            var b = await SeedServerAsync(db, "b");

            var bytes = Jar("shared by two servers");
            var sha = Sha256Of(bytes);
            var (stored, size) = await blobs.StoreAsync(new MemoryStream(bytes), TestLimits.MaxBytes);

            var elsewhere = new Mod
            {
                ServerId = b,
                FileName = "already-here.jar",
                Sha256 = stored,
                Size = size,
            };

            db.Mods.Add(elsewhere);
            await db.SaveChangesAsync();

            var collecting = new CollectingBlobs(blobs, sha, async () =>
            {
                await using var deleter = PostgresHarness.Context(connectionString);
                var row = await deleter.Mods.SingleAsync(m => m.ServerId == b);
                deleter.Mods.Remove(row);
                await deleter.SaveChangesAsync();

                await BlobCollector.CollectAsync(deleter, blobs, sha);
            });

            var client = new FakeModrinthClient();
            client.AddDownloadableMod("PA", "v-a", "A", "a.jar", bytes);

            var result = await new InstallModrinthModsCommandHandler(
                    db, collecting, client, new StubUser("alex"), TestLimits.Config)
                .Handle(new InstallModrinthModsCommand(a, [new ModrinthInstallItem("v-a", false)]),
                    CancellationToken.None);

            await Assert.That(collecting.Collected).IsTrue();
            await Assert.That(result.Installed).Count().IsEqualTo(1);

            var row = await db.Mods.AsNoTracking().SingleAsync(m => m.ServerId == a);
            await Assert.That(row.Sha256).IsEqualTo(sha);
            await Assert.That(blobs.Exists(row.Sha256)).IsTrue();
        }

        [Test]
        public async Task Install_HashMismatch_NeverPublishesTheDownloadedBytes()
        {
            using var dir = new TempDir();
            await using var db = PostgresHarness.Context(await PostgresHarness.NewMigratedDatabaseAsync());

            var blobs = StorageIn(dir.Path);
            var serverId = await SeedServerAsync(db, "a");

            var bytes = Jar("tampered");
            var client = new FakeModrinthClient();
            client.AddDownloadableMod("PA", "v-a", "A", "a.jar", bytes, publishedSha1: new string('0', 40));

            var result = await new InstallModrinthModsCommandHandler(
                    db, blobs, client, new StubUser("alex"), TestLimits.Config)
                .Handle(new InstallModrinthModsCommand(serverId, [new ModrinthInstallItem("v-a", false)]),
                    CancellationToken.None);

            await Assert.That(result.Failed).Count().IsEqualTo(1);
            await Assert.That(blobs.Exists(Sha256Of(bytes))).IsFalse();
            await Assert.That(blobs.EnumerateScratch().ToList()).IsEmpty();
        }

        private sealed class CollectingBlobs(IBlobStorage inner, string sha256, Func<Task> beforeTheRowLands) : IBlobStorage
        {
            private bool _done;

            public bool Collected { get; private set; }

            public Task<StagedBlob> StageAsync(Stream content, long maxBytes, CancellationToken cancellationToken = default) =>
                inner.StageAsync(content, maxBytes, cancellationToken);

            public void Promote(StagedBlob staged) => inner.Promote(staged);

            public void Discard(StagedBlob staged) => inner.Discard(staged);

            public Stream OpenStaged(StagedBlob staged)
            {
                Race();
                return inner.OpenStaged(staged);
            }

            public Stream? OpenRead(string sha256)
            {
                Race();
                return inner.OpenRead(sha256);
            }

            private void Race()
            {
                if (_done)
                    return;

                _done = true;

                beforeTheRowLands().GetAwaiter().GetResult();
                Collected = !inner.Exists(sha256);
            }

            public bool Exists(string sha256) => inner.Exists(sha256);

            public void Delete(string sha256) => inner.Delete(sha256);

            public IEnumerable<StoredBlob> EnumerateBlobs() => inner.EnumerateBlobs();

            public IEnumerable<ScratchFile> EnumerateScratch() => inner.EnumerateScratch();
        }
    }
}
