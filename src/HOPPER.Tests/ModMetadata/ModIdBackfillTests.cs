using HOPPER.Application.ModMetadata;
using HOPPER.Domain;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HOPPER.Tests.ModMetadata
{
    /// <summary>
    /// Every row on every already-deployed server carries a null ModIds, and nothing re-uploads
    /// those jars: the blob is on disk, the row is correct in every other respect, and without a
    /// backfill the client would keep colliding with the player's own copy forever. So the feature
    /// does nothing on exactly the installs that need it unless this runs.
    ///
    /// The distinction it has to preserve is null versus empty. Null is "we have not looked" and is
    /// retried; empty is "we looked and it declares nothing" and is final. Collapsing the two would
    /// turn a blob that was briefly unreadable into a permanent answer.
    /// </summary>
    public class ModIdBackfillTests
    {
        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hopper-backfill-" + Guid.NewGuid().ToString("N"));
            public TempDir() => Directory.CreateDirectory(Path);
            public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { /* temp */ } }
        }

        /// <summary>The service resolves a DbContext and a blob store per batch, so the test wires a
        /// container holding exactly those two rather than booting the API.</summary>
        private sealed class Fixture : IDisposable
        {
            public TempDir Dir { get; } = new();
            public HopperDbContext Db { get; }
            public IBlobStorage Blobs { get; }
            public ServiceProvider Services { get; }
            public Guid ServerId { get; } = Guid.NewGuid();

            public Fixture(IBlobStorage? blobs = null)
            {
                Db = new HopperDbContext(new DbContextOptionsBuilder<HopperDbContext>()
                    .UseInMemoryDatabase($"hopper-backfill-{Guid.NewGuid():N}")
                    .Options);

                Blobs = blobs ?? new FileSystemBlobStorage(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Blobs:Directory"] = Dir.Path })
                    .Build());

                // Both registered as instances so the container does not dispose the context when
                // the service's per-batch scope ends. In the API these are a scoped DbContext and a
                // singleton blob store; here one context has to survive several batches so the test
                // can assert on the tracked entities it seeded.
                var services = new ServiceCollection();
                services.AddSingleton(Db);
                services.AddSingleton(Blobs);
                Services = services.BuildServiceProvider();
            }

            /// <summary>StartAsync returns as soon as ExecuteAsync first yields, so the pass itself
            /// is awaited through ExecuteTask. Asserting before that is a race the test would lose
            /// intermittently rather than loudly.</summary>
            public async Task RunAsync()
            {
                var service = new ModIdBackfillService(
                    Services.GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<ModIdBackfillService>.Instance);

                await service.StartAsync(CancellationToken.None);

                if (service.ExecuteTask is not null)
                    await service.ExecuteTask;
            }

            public async Task<Mod> SeedAsync(string fileName, byte[]? bytes, string[]? modIds)
            {
                var sha = new string('a', 63) + (char)('a' + fileName.Length % 6);

                if (bytes is not null)
                {
                    await using var stream = new MemoryStream(bytes);
                    (sha, _) = await Blobs.SaveAsync(stream, CancellationToken.None);
                }

                var row = new Mod
                {
                    ServerId = ServerId,
                    FileName = fileName,
                    Sha256 = sha,
                    Size = bytes?.Length ?? 0,
                    ModIds = modIds,
                };

                Db.Mods.Add(row);
                await Db.SaveChangesAsync();
                return row;
            }

            public void Dispose()
            {
                Services.Dispose();
                Db.Dispose();
                Dir.Dispose();
            }
        }

        [Test]
        public async Task Backfill_RowWithNullModIds_IsFilledFromTheBlob()
        {
            using var fixture = new Fixture();
            var row = await fixture.SeedAsync("jei.jar", ModIdExtractionTests.ForgeJar("jei"), modIds: null);

            await fixture.RunAsync();

            await Assert.That(row.ModIds).IsEquivalentTo(new[] { "jei" });
        }

        [Test]
        public async Task Backfill_RowWhoseJarDeclaresNothing_BecomesEmptyRatherThanStayingNull()
        {
            // The library case. It has to end as empty, or every boot forever would re-open the
            // same blob to learn the same nothing.
            using var fixture = new Fixture();
            var row = await fixture.SeedAsync("lib.jar", ModIdExtractionTests.Zip(("a/B.class", "x")), modIds: null);

            await fixture.RunAsync();

            await Assert.That(row.ModIds).IsNotNull();
            await Assert.That(row.ModIds!).IsEmpty();
        }

        [Test]
        public async Task Backfill_RowWithAnEmptyModIdSet_IsLeftAlone()
        {
            // Idempotence: a second pass must find nothing to do.
            using var fixture = new Fixture();
            var row = await fixture.SeedAsync("jei.jar", ModIdExtractionTests.ForgeJar("jei"), modIds: []);

            await fixture.RunAsync();

            // Not refilled from the blob even though the blob would have yielded "jei". Empty is a
            // decision that was already taken.
            await Assert.That(row.ModIds!).IsEmpty();
        }

        [Test]
        public async Task Backfill_RowThatAlreadyHasIds_IsNotRewritten()
        {
            using var fixture = new Fixture();
            var row = await fixture.SeedAsync("jei.jar", ModIdExtractionTests.ForgeJar("jei"), modIds: ["pinned"]);

            await fixture.RunAsync();

            await Assert.That(row.ModIds).IsEquivalentTo(new[] { "pinned" });
        }

        [Test]
        public async Task Backfill_RowWhoseBlobIsMissing_StaysNullSoItIsRetried()
        {
            using var fixture = new Fixture();
            var row = await fixture.SeedAsync("gone.jar", bytes: null, modIds: null);

            await fixture.RunAsync();

            await Assert.That(row.ModIds).IsNull();
        }

        [Test]
        public async Task Backfill_MoreRowsThanOneBatch_FillsThemAll()
        {
            // The batch size is 200, so 250 rows crosses it and proves the paging does not stall or
            // repeat. Every jar carries a distinct id, so a mis-paged run shows up as a wrong id
            // rather than only as a wrong count.
            using var fixture = new Fixture();

            for (var i = 0; i < 250; i++)
                await fixture.SeedAsync($"mod{i}.jar", ModIdExtractionTests.ForgeJar($"mod{i}"), modIds: null);

            await fixture.RunAsync();

            var rows = await fixture.Db.Mods.ToListAsync();
            await Assert.That(rows.Count(r => r.ModIds is { Length: 1 })).IsEqualTo(250);
            await Assert.That(rows.Single(r => r.FileName == "mod249.jar").ModIds).IsEquivalentTo(new[] { "mod249" });
        }

        private sealed class ThrowingBlobs : IBlobStorage
        {
            public Task<(string Sha256, long Size)> SaveAsync(Stream content, CancellationToken cancellationToken = default) =>
                Task.FromResult((new string('a', 64), 0L));

            public Stream? OpenRead(string sha256) => throw new IOException("the volume went away");

            public bool Exists(string sha256) => false;

            public void Delete(string sha256) { }
        }

        [Test]
        public async Task Backfill_WhenTheBlobStoreThrows_DoesNotPropagateAndLeavesRowsNull()
        {
            // A backfill that cannot run is a dormant feature, not an API that fails to start. The
            // rows stay null, which is exactly the state the next boot retries.
            using var fixture = new Fixture(new ThrowingBlobs());
            var row = await fixture.SeedAsync("jei.jar", bytes: null, modIds: null);

            await fixture.RunAsync();

            await Assert.That(row.ModIds).IsNull();
        }
    }
}
