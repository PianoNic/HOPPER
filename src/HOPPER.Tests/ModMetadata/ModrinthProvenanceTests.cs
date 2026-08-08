using HOPPER.Application.ModMetadata;
using HOPPER.Application.Modrinth;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Tests.Modrinth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace HOPPER.Tests.ModMetadata
{
    public class ModrinthProvenanceTests
    {
        private sealed class Fixture : IDisposable
        {
            public HopperDbContext Db { get; }

            public FakeModrinthClient Client { get; } = new();

            public Guid ServerId { get; } = Guid.NewGuid();

            private IServiceProvider Services { get; }

            public Fixture()
            {
                Db = new HopperDbContext(new DbContextOptionsBuilder<HopperDbContext>()
                    .UseInMemoryDatabase($"hopper-provenance-{Guid.NewGuid():N}")
                    .Options);

                var services = new ServiceCollection();
                services.AddSingleton(Db);
                services.AddSingleton<IModrinthClient>(Client);
                Services = services.BuildServiceProvider();
            }

            public async Task RunAsync()
            {
                var service = new ModrinthProvenanceService(
                    Services.GetRequiredService<IServiceScopeFactory>(),
                    NullLogger<ModrinthProvenanceService>.Instance);

                await service.StartAsync(CancellationToken.None);

                if (service.ExecuteTask is not null)
                    await service.ExecuteTask;
            }

            public async Task<Mod> SeedAsync(string fileName, string? sha512, string? projectId = null,
                DateTime? checkedAt = null)
            {
                var row = new Mod
                {
                    ServerId = ServerId,
                    FileName = fileName,
                    Sha256 = new string('a', 63) + (char)('a' + fileName.Length % 6),
                    Size = 10,
                    Sha512 = sha512,
                    ProjectId = projectId,
                    ProvenanceCheckedAt = checkedAt,
                    Source = projectId is null ? ModSource.Manual : ModSource.Modrinth,
                };

                Db.Mods.Add(row);
                await Db.SaveChangesAsync();

                return row;
            }

            public void Dispose() => Db.Dispose();
        }

        [Test]
        public async Task AnUploadedJarThatIsAModrinthRelease_TakesOnItsIdentity()
        {
            using var fixture = new Fixture();
            var row = await fixture.SeedAsync("jei.jar", sha512: "abc123");

            var version = fixture.Client.AddMod("u6dRKJwZ", "v-jei", "JEI", "jei.jar");
            fixture.Client.ByHash["abc123"] = version;

            await fixture.RunAsync();

            await Assert.That(row.ProjectId).IsEqualTo("u6dRKJwZ");
            await Assert.That(row.VersionId).IsEqualTo("v-jei");
            await Assert.That(row.Source).IsEqualTo(ModSource.Modrinth);

            // The whole point: a jar that had no source now has one to re-download from.
            await Assert.That(row.DownloadUrl).IsNotNull();
            await Assert.That(row.ProvenanceCheckedAt).IsNotNull();
        }

        [Test]
        public async Task AnAdoptedJar_GetsTheProjectNameAndIconToShowBesideIt()
        {
            using var fixture = new Fixture();
            var row = await fixture.SeedAsync("jei.jar", sha512: "abc123");

            var version = fixture.Client.AddMod("u6dRKJwZ", "v-jei", "Just Enough Items", "jei.jar");
            fixture.Client.ByHash["abc123"] = version;

            await fixture.RunAsync();

            // Without these the row reads as Modrinth with nothing beside it.
            await Assert.That(row.ProjectName).IsEqualTo("Just Enough Items");
        }

        [Test]
        public async Task NothingMatched_MeansNoProjectLookupAtAll()
        {
            using var fixture = new Fixture();
            await fixture.SeedAsync("my-own.jar", sha512: "no-match");

            await fixture.RunAsync();

            await Assert.That(fixture.Client.ProjectLookups).IsEmpty();
        }

        [Test]
        public async Task AJarModrinthDoesNotPublish_IsMarkedAskedRatherThanAskedForever()
        {
            using var fixture = new Fixture();
            var row = await fixture.SeedAsync("my-own.jar", sha512: "nothing-matches-this");

            await fixture.RunAsync();

            await Assert.That(row.ProjectId).IsNull();
            await Assert.That(row.Source).IsEqualTo(ModSource.Manual);
            await Assert.That(row.ProvenanceCheckedAt).IsNotNull();
        }

        [Test]
        public async Task AJarAskedAboutRecently_IsNotAskedAgain()
        {
            using var fixture = new Fixture();
            await fixture.SeedAsync("my-own.jar", sha512: "nope", checkedAt: DateTime.UtcNow.AddDays(-1));

            await fixture.RunAsync();

            await Assert.That(fixture.Client.HashLookups).IsEmpty();
        }

        [Test]
        public async Task AJarAskedAboutLongAgo_IsWorthAnotherLook()
        {
            using var fixture = new Fixture();
            await fixture.SeedAsync("my-own.jar", sha512: "nope", checkedAt: DateTime.UtcNow.AddDays(-60));

            await fixture.RunAsync();

            await Assert.That(fixture.Client.HashLookups).IsNotEmpty();
        }

        [Test]
        public async Task AJarThatAlreadyKnowsWhatItIs_IsNeverAskedAbout()
        {
            using var fixture = new Fixture();
            await fixture.SeedAsync("jei.jar", sha512: "abc123", projectId: "u6dRKJwZ");

            await fixture.RunAsync();

            await Assert.That(fixture.Client.HashLookups).IsEmpty();
        }

        [Test]
        public async Task AJarWithNoSha512_IsNotAskedAbout()
        {
            using var fixture = new Fixture();
            await fixture.SeedAsync("old.jar", sha512: null);

            await fixture.RunAsync();

            await Assert.That(fixture.Client.HashLookups).IsEmpty();
        }

        [Test]
        public async Task EveryUnknownJar_GoesOutInOneRequest()
        {
            using var fixture = new Fixture();

            for (var i = 0; i < 5; i++)
                await fixture.SeedAsync($"mod-{i}.jar", sha512: $"hash-{i}");

            await fixture.RunAsync();

            await Assert.That(fixture.Client.HashLookups.Count).IsEqualTo(1);
            await Assert.That(fixture.Client.HashLookups[0].Count).IsEqualTo(5);
        }
    }
}
