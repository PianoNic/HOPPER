using System.Text.Json;
using HOPPER.Application.Queries.Manifest;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Tests.Application
{
    public class ManifestSideFilterTests
    {
        private static readonly Guid ServerId = Guid.NewGuid();

        private static HopperDbContext NewDb() =>
            new(new DbContextOptionsBuilder<HopperDbContext>()
                .UseInMemoryDatabase($"hopper-{Guid.NewGuid():N}")
                .Options);

        private static Mod Row(string file, char fill, ModSide side) =>
            new() { ServerId = ServerId, FileName = file, Sha256 = new string(fill, 64), Size = 10, Side = side };

        private static async Task<HopperDbContext> WithOneOfEach()
        {
            var db = NewDb();
            db.Mods.AddRange(
                Row("both.jar", 'a', ModSide.Both),
                Row("client-only.jar", 'b', ModSide.ClientOnly),
                Row("server-only.jar", 'c', ModSide.ServerOnly));
            await db.SaveChangesAsync();
            return db;
        }

        private static async Task<IReadOnlyList<string>> Files(HopperDbContext db, SyncSide side)
        {
            var result = await new GetManifestQueryHandler(db)
                .Handle(new GetManifestQuery(ServerId, "https://h", side), CancellationToken.None);
            return result.Mods.Select(m => m.File).ToList();
        }

        [Test]
        public async Task Client_GetsBothAndClientOnly()
        {
            await using var db = await WithOneOfEach();

            await Assert.That(await Files(db, SyncSide.Client))
                .IsEquivalentTo(new[] { "both.jar", "client-only.jar" });
        }

        [Test]
        public async Task Server_GetsBothAndServerOnly()
        {
            await using var db = await WithOneOfEach();

            await Assert.That(await Files(db, SyncSide.Server))
                .IsEquivalentTo(new[] { "both.jar", "server-only.jar" });
        }

        [Test]
        public async Task NoSideGiven_IsTheClientSet()
        {
            await using var db = await WithOneOfEach();

            var result = await new GetManifestQueryHandler(db)
                .Handle(new GetManifestQuery(ServerId, "https://h"), CancellationToken.None);

            await Assert.That(result.Mods.Select(m => m.File).ToList())
                .IsEquivalentTo(new[] { "both.jar", "client-only.jar" });
        }

        [Test]
        public async Task EveryModIsBoth_BothSidesSeeAllOfThem()
        {
            await using var db = NewDb();
            db.Mods.AddRange(Row("a.jar", 'a', ModSide.Both), Row("b.jar", 'b', ModSide.Both));
            await db.SaveChangesAsync();

            await Assert.That(await Files(db, SyncSide.Client)).IsEquivalentTo(new[] { "a.jar", "b.jar" });
            await Assert.That(await Files(db, SyncSide.Server)).IsEquivalentTo(new[] { "a.jar", "b.jar" });
        }

        [Test]
        public async Task TheEntryShapeIsIdenticalOnBothSides()
        {
            await using var db = NewDb();
            db.Mods.Add(new Mod
            {
                ServerId = ServerId,
                FileName = "both.jar",
                Sha256 = new string('a', 64),
                Size = 10,
                Side = ModSide.Both,
                ModIds = ["jei"],
            });
            await db.SaveChangesAsync();

            var handler = new GetManifestQueryHandler(db);
            var client = await handler.Handle(new GetManifestQuery(ServerId, "https://h", SyncSide.Client), CancellationToken.None);
            var server = await handler.Handle(new GetManifestQuery(ServerId, "https://h", SyncSide.Server), CancellationToken.None);

            static (List<string> Keys, List<JsonValueKind> Kinds) Shape(object manifest)
            {
                using var document = JsonDocument.Parse(JsonSerializer.Serialize(manifest));
                var entry = document.RootElement.GetProperty("mods")[0];
                return (entry.EnumerateObject().Select(p => p.Name).ToList(),
                        entry.EnumerateObject().Select(p => p.Value.ValueKind).ToList());
            }

            var clientShape = Shape(client);
            var serverShape = Shape(server);

            await Assert.That(clientShape.Keys).IsEquivalentTo(serverShape.Keys);
            await Assert.That(clientShape.Kinds).IsEquivalentTo(serverShape.Kinds);
            await Assert.That(clientShape.Keys).IsEquivalentTo(new[] { "file", "url", "sha256", "size", "modIds" });
            await Assert.That(clientShape.Kinds[3]).IsEqualTo(JsonValueKind.Number);
        }

        [Test]
        public async Task Server_BlobUrlsCarryTheSide()
        {
            await using var db = await WithOneOfEach();

            var result = await new GetManifestQueryHandler(db)
                .Handle(new GetManifestQuery(ServerId, "https://h", SyncSide.Server), CancellationToken.None);

            await Assert.That(result.Mods.All(m => m.Url.EndsWith("?side=server", StringComparison.Ordinal))).IsTrue();
        }

        [Test]
        public async Task Client_BlobUrlsAreUnchanged()
        {
            await using var db = await WithOneOfEach();

            var result = await new GetManifestQueryHandler(db)
                .Handle(new GetManifestQuery(ServerId, "https://h"), CancellationToken.None);

            await Assert.That(result.Mods.All(m => !m.Url.Contains('?'))).IsTrue();
        }

        [Test]
        [Arguments(ModSide.Both, SyncSide.Client, true)]
        [Arguments(ModSide.Both, SyncSide.Server, true)]
        [Arguments(ModSide.ClientOnly, SyncSide.Client, true)]
        [Arguments(ModSide.ClientOnly, SyncSide.Server, false)]
        [Arguments(ModSide.ServerOnly, SyncSide.Client, false)]
        [Arguments(ModSide.ServerOnly, SyncSide.Server, true)]
        public async Task Reaches_IsTheWholeTable(ModSide mod, SyncSide caller, bool expected)
        {
            await Assert.That(ModSideRules.Reaches(mod, caller)).IsEqualTo(expected);
        }

        [Test]
        [Arguments(null, SyncSide.Client)]
        [Arguments("", SyncSide.Client)]
        [Arguments("   ", SyncSide.Client)]
        [Arguments("client", SyncSide.Client)]
        [Arguments("Client", SyncSide.Client)]
        [Arguments("SERVER", SyncSide.Server)]
        [Arguments("server", SyncSide.Server)]
        public async Task TryParse_AcceptedValues(string? value, SyncSide expected)
        {
            await Assert.That(ModSideRules.TryParse(value, out var side)).IsTrue();
            await Assert.That(side).IsEqualTo(expected);
        }

        [Test]
        [Arguments("both")]
        [Arguments("dedicated")]
        [Arguments("1")]
        [Arguments("clientt")]
        public async Task TryParse_AnythingElseIsRefused(string value)
        {
            await Assert.That(ModSideRules.TryParse(value, out _)).IsFalse();
        }
    }
}
