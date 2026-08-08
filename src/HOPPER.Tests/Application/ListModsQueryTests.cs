using System.Text;
using HOPPER.Application.Queries.Mods;
using HOPPER.Domain;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Services;
using HOPPER.Tests.Infrastructure;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Tests.Application
{
    public class ListModsQueryTests
    {
        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hopper-listmods-" + Guid.NewGuid().ToString("N"));
            public TempDir() => Directory.CreateDirectory(Path);
            public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
        }

        private static FileSystemBlobStorage StorageIn(string root) =>
            new(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Blobs:Directory"] = root })
                .Build());

        private static async Task<Guid> SeedServerAsync(HopperDbContext db)
        {
            var server = new Server
            {
                Name = "List Mods",
                Slug = "list-mods-" + Guid.NewGuid().ToString("N")[..8],
                Token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            };

            db.Servers.Add(server);
            await db.SaveChangesAsync();

            return server.Id;
        }

        [Test]
        public async Task AModWhoseBytesAreGone_IsFlaggedAndTheRestAreNot()
        {
            using var dir = new TempDir();
            var blobs = StorageIn(dir.Path);

            var connectionString = await PostgresHarness.NewMigratedDatabaseAsync();
            await using var db = PostgresHarness.Context(connectionString);

            var serverId = await SeedServerAsync(db);

            var (present, size) = await blobs.StoreAsync(
                new MemoryStream(Encoding.UTF8.GetBytes("PK here")), TestLimits.MaxBytes);

            db.Mods.AddRange(
                new Mod { ServerId = serverId, FileName = "here.jar", Sha256 = present, Size = size },
                new Mod { ServerId = serverId, FileName = "gone.jar", Sha256 = new string('a', 64), Size = 10 });

            await db.SaveChangesAsync();

            var rows = await new ListModsQueryHandler(db, blobs)
                .Handle(new ListModsQuery(serverId), CancellationToken.None);

            await Assert.That(rows.Single(r => r.FileName == "here.jar").BytesMissing).IsFalse();
            await Assert.That(rows.Single(r => r.FileName == "gone.jar").BytesMissing).IsTrue();
        }

        [Test]
        public async Task OnlyTheCollidingPairCarriesASide_AndEveryoneElseCarriesNone()
        {
            using var dir = new TempDir();
            var blobs = StorageIn(dir.Path);

            var connectionString = await PostgresHarness.NewMigratedDatabaseAsync();
            await using var db = PostgresHarness.Context(connectionString);

            var serverId = await SeedServerAsync(db);

            db.Mods.AddRange(
                new Mod { ServerId = serverId, FileName = "jade.jar", Sha256 = new string('c', 64), Size = 1, ModIds = ["jade"] },
                new Mod { ServerId = serverId, FileName = "jade-copy.jar", Sha256 = new string('d', 64), Size = 1, ModIds = ["jade"] },
                new Mod { ServerId = serverId, FileName = "jei.jar", Sha256 = new string('e', 64), Size = 1, ModIds = ["jei"] });

            await db.SaveChangesAsync();

            var rows = await new ListModsQueryHandler(db, blobs)
                .Handle(new ListModsQuery(serverId), CancellationToken.None);

            await Assert.That(rows.Single(r => r.FileName == "jade.jar").CollidesOn).IsNotNull();
            await Assert.That(rows.Single(r => r.FileName == "jade-copy.jar").CollidesOn).IsNotNull();

            // The one that regressed: an absent key defaults to SyncSide.Client, not to null.
            await Assert.That(rows.Single(r => r.FileName == "jei.jar").CollidesOn).IsNull();
        }

        [Test]
        public async Task ADependencyNothingOnTheServerProvides_IsNamed()
        {
            using var dir = new TempDir();
            var blobs = StorageIn(dir.Path);

            var connectionString = await PostgresHarness.NewMigratedDatabaseAsync();
            await using var db = PostgresHarness.Context(connectionString);

            var serverId = await SeedServerAsync(db);

            db.Mods.AddRange(
                new Mod
                {
                    ServerId = serverId, FileName = "entityculling.jar", Sha256 = new string('1', 64), Size = 1,
                    ModIds = ["entityculling"],
                    // minecraft is the loader's, ferritecore is here, fabric-api is not.
                    RequiredMods = ["fabric-api", "minecraft", "ferritecore"],
                },
                new Mod
                {
                    ServerId = serverId, FileName = "ferritecore.jar", Sha256 = new string('2', 64), Size = 1,
                    ModIds = ["ferritecore"], RequiredMods = [],
                });

            await db.SaveChangesAsync();

            var rows = await new ListModsQueryHandler(db, blobs)
                .Handle(new ListModsQuery(serverId), CancellationToken.None);

            var culling = rows.Single(r => r.FileName == "entityculling.jar");

            await Assert.That(culling.MissingDependencies).IsEquivalentTo(new[] { "fabric-api" });
            await Assert.That(rows.Single(r => r.FileName == "ferritecore.jar").MissingDependencies).IsNull();
        }

        [Test]
        public async Task AJarWhoseDependenciesWereNeverRead_ClaimsNothingIsMissing()
        {
            using var dir = new TempDir();
            var blobs = StorageIn(dir.Path);

            var connectionString = await PostgresHarness.NewMigratedDatabaseAsync();
            await using var db = PostgresHarness.Context(connectionString);

            var serverId = await SeedServerAsync(db);

            db.Mods.Add(new Mod
            {
                ServerId = serverId, FileName = "unread.jar", Sha256 = new string('3', 64), Size = 1,
                RequiredMods = null,
            });

            await db.SaveChangesAsync();

            var rows = await new ListModsQueryHandler(db, blobs)
                .Handle(new ListModsQuery(serverId), CancellationToken.None);

            await Assert.That(rows.Single().MissingDependencies).IsNull();
        }

        [Test]
        public async Task TwoNamesForOneBlob_AreBothFlaggedFromASingleLookup()
        {
            using var dir = new TempDir();
            var blobs = StorageIn(dir.Path);

            var connectionString = await PostgresHarness.NewMigratedDatabaseAsync();
            await using var db = PostgresHarness.Context(connectionString);

            var serverId = await SeedServerAsync(db);
            var sha = new string('b', 64);

            db.Mods.AddRange(
                new Mod { ServerId = serverId, FileName = "one.jar", Sha256 = sha, Size = 10 },
                new Mod { ServerId = serverId, FileName = "two.jar", Sha256 = sha, Size = 10 });

            await db.SaveChangesAsync();

            var rows = await new ListModsQueryHandler(db, blobs)
                .Handle(new ListModsQuery(serverId), CancellationToken.None);

            await Assert.That(rows.Count).IsEqualTo(2);
            await Assert.That(rows.All(r => r.BytesMissing)).IsTrue();
        }
    }
}
