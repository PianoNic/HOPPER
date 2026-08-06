using HOPPER.Application.Queries.Manifest;
using HOPPER.Domain;
using HOPPER.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Tests.Application
{
    public class GetManifestQueryHandlerTests
    {
        /// <summary>The server every row in these tests belongs to. A manifest is per-server now, so
        /// the query needs one; which server it is does not matter to any assertion here.</summary>
        private static readonly Guid ServerId = Guid.NewGuid();

        private static HopperDbContext NewDb() =>
            new(new DbContextOptionsBuilder<HopperDbContext>()
                .UseInMemoryDatabase($"hopper-{Guid.NewGuid():N}")
                .Options);

        private static Mod Row(string file, string sha, long size = 10) =>
            new() { ServerId = ServerId, FileName = file, Sha256 = sha, Size = size };

        [Test]
        public async Task Handle_Mod_BuildsTheBlobUrlFromTheHash()
        {
            await using var db = NewDb();
            db.Mods.Add(Row("jei.jar", new string('a', 64), 1234567));
            await db.SaveChangesAsync();

            var result = await new GetManifestQueryHandler(db)
                .Handle(new GetManifestQuery(ServerId, "https://hopper.example.com"), CancellationToken.None);

            var entry = result.Mods.Single();
            await Assert.That(entry.File).IsEqualTo("jei.jar");
            await Assert.That(entry.Url).IsEqualTo($"https://hopper.example.com/api/blobs/{new string('a', 64)}");
            await Assert.That(entry.Sha256).IsEqualTo(new string('a', 64));
            await Assert.That(entry.Size).IsEqualTo(1234567L);
        }

        [Test]
        public async Task Handle_BaseUrlWithATrailingSlash_DoesNotDoubleUpTheSeparator()
        {
            // Hopper:PublicBaseUrl is typed by a human. "https://host//api/blobs/..." resolves on most
            // servers but is a different URL to a cache and reads as a bug in the client's logs.
            await using var db = NewDb();
            db.Mods.Add(Row("jei.jar", new string('b', 64)));
            await db.SaveChangesAsync();

            var result = await new GetManifestQueryHandler(db)
                .Handle(new GetManifestQuery(ServerId, "https://hopper.example.com/"), CancellationToken.None);

            await Assert.That(result.Mods.Single().Url)
                .IsEqualTo($"https://hopper.example.com/api/blobs/{new string('b', 64)}");
        }

        [Test]
        public async Task Handle_SeveralMods_AreOrderedByFileName()
        {
            // Two manifests taken at different times have to diff meaningfully, and a client comparing
            // responses must never see a change that is only row ordering.
            await using var db = NewDb();
            db.Mods.AddRange(Row("zzz.jar", new string('c', 64)), Row("aaa.jar", new string('d', 64)), Row("mmm.jar", new string('e', 64)));
            await db.SaveChangesAsync();

            var result = await new GetManifestQueryHandler(db)
                .Handle(new GetManifestQuery(ServerId, "http://localhost:5170"), CancellationToken.None);

            await Assert.That(result.Mods.Select(m => m.File).ToList())
                .IsEquivalentTo(new[] { "aaa.jar", "mmm.jar", "zzz.jar" });
        }

        [Test]
        public async Task Handle_NoMods_ReturnsAnEmptyListRatherThanNull()
        {
            await using var db = NewDb();

            var result = await new GetManifestQueryHandler(db)
                .Handle(new GetManifestQuery(ServerId, "http://localhost:5170"), CancellationToken.None);

            await Assert.That(result.Mods).IsNotNull();
            await Assert.That(result.Mods).IsEmpty();
        }
    }
}
