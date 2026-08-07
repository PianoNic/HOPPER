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
    public class ModIdBackfillTests
    {
        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hopper-backfill-" + Guid.NewGuid().ToString("N"));
            public TempDir() => Directory.CreateDirectory(Path);
            public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch {  } }
        }

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

                var services = new ServiceCollection();
                services.AddSingleton(Db);
                services.AddSingleton(Blobs);
                Services = services.BuildServiceProvider();
            }

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
                    (sha, _) = await Blobs.StoreAsync(stream, TestLimits.MaxBytes, CancellationToken.None);
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
            using var fixture = new Fixture();
            var row = await fixture.SeedAsync("lib.jar", ModIdExtractionTests.Zip(("a/B.class", "x")), modIds: null);

            await fixture.RunAsync();

            await Assert.That(row.ModIds).IsNotNull();
            await Assert.That(row.ModIds!).IsEmpty();
        }

        [Test]
        public async Task Backfill_RowWithAnEmptyModIdSet_IsLeftAlone()
        {
            using var fixture = new Fixture();
            var row = await fixture.SeedAsync("jei.jar", ModIdExtractionTests.ForgeJar("jei"), modIds: []);

            await fixture.RunAsync();

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
            public Task<StagedBlob> StageAsync(Stream content, long maxBytes, CancellationToken cancellationToken = default) =>
                Task.FromResult(new StagedBlob(new string('a', 64), 0L, "unused"));

            public void Promote(StagedBlob staged) { }

            public void Discard(StagedBlob staged) { }

            public Stream OpenStaged(StagedBlob staged) => throw new IOException("the volume went away");

            public Stream? OpenRead(string sha256) => throw new IOException("the volume went away");

            public bool Exists(string sha256) => false;

            public void Delete(string sha256) { }

            public IEnumerable<StoredBlob> EnumerateBlobs() => [];

            public IEnumerable<ScratchFile> EnumerateScratch() => [];
        }

        [Test]
        public async Task Backfill_WhenTheBlobStoreThrows_DoesNotPropagateAndLeavesRowsNull()
        {
            using var fixture = new Fixture(new ThrowingBlobs());
            var row = await fixture.SeedAsync("jei.jar", bytes: null, modIds: null);

            await fixture.RunAsync();

            await Assert.That(row.ModIds).IsNull();
        }
    }
}
