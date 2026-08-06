using System.Text.Json;
using HOPPER.Application.Dtos.Manifest;
using HOPPER.Application.Queries.Manifest;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Tests.Wire
{
    public class ManifestWireFormatTests
    {
        private static ManifestDto SampleManifest() => new()
        {
            Mods =
            [
                new ManifestModDto
                {
                    File = "jei-1.20.1-15.2.0.27.jar",
                    Url = "https://hopper.example.com/api/blobs/" + new string('a', 64),
                    Sha256 = new string('a', 64),
                    Size = 1234567,
                },
            ],
        };

        [Test]
        public async Task Serialize_Manifest_ProducesExactlyTheShippedShape()
        {
            var json = JsonSerializer.Serialize(SampleManifest());

            await Assert.That(json).IsEqualTo(
                """{"mods":[{"file":"jei-1.20.1-15.2.0.27.jar","url":"https://hopper.example.com/api/blobs/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":1234567}]}""");
        }

        [Test]
        [Arguments("mods")]
        [Arguments("file")]
        [Arguments("url")]
        [Arguments("sha256")]
        [Arguments("size")]
        public async Task Serialize_EveryFieldName_SurvivesACamelCaseNamingPolicy(string expectedName)
        {
            var json = JsonSerializer.Serialize(
                SampleManifest(),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var entry = root.GetProperty("mods")[0];

            await Assert.That(expectedName == "mods" ? root.TryGetProperty(expectedName, out _)
                                                     : entry.TryGetProperty(expectedName, out _)).IsTrue();
        }

        [Test]
        public async Task Serialize_EveryFieldName_SurvivesASnakeCaseNamingPolicy()
        {
            var json = JsonSerializer.Serialize(
                SampleManifest(),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

            using var document = JsonDocument.Parse(json);
            var entry = document.RootElement.GetProperty("mods")[0];

            await Assert.That(entry.GetProperty("file").GetString()).IsEqualTo("jei-1.20.1-15.2.0.27.jar");
            await Assert.That(entry.GetProperty("sha256").GetString()).IsEqualTo(new string('a', 64));
        }

        [Test]
        public async Task Serialize_Size_IsAJsonNumberNotAString()
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(SampleManifest()));
            var size = document.RootElement.GetProperty("mods")[0].GetProperty("size");

            await Assert.That(size.ValueKind).IsEqualTo(JsonValueKind.Number);
            await Assert.That(size.GetInt64()).IsEqualTo(1234567L);
        }

        [Test]
        public async Task Serialize_Root_CarriesModsAndNothingElse()
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(SampleManifest()));

            var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToList();
            await Assert.That(names).IsEquivalentTo(new[] { "mods" });
        }

        [Test]
        public async Task Serialize_ManifestEntry_CarriesExactlyFourFields()
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(SampleManifest()));

            var names = document.RootElement.GetProperty("mods")[0].EnumerateObject().Select(p => p.Name).ToList();
            await Assert.That(names).IsEquivalentTo(new[] { "file", "url", "sha256", "size" });
        }

        [Test]
        public async Task Serialize_ModWithFullModrinthProvenance_LooksIdenticalOnTheWire()
        {
            var db = new HopperDbContext(new DbContextOptionsBuilder<HopperDbContext>()
                .UseInMemoryDatabase($"hopper-wire-{Guid.NewGuid():N}")
                .Options);

            await using (db)
            {
                var serverId = Guid.NewGuid();

                db.Mods.Add(new Mod
                {
                    ServerId = serverId,
                    FileName = "jei-1.20.1-15.2.0.27.jar",
                    Sha256 = new string('a', 64),
                    Size = 1234567,
                    Source = ModSource.Modrinth,
                    ProjectId = "u6dRKJwZ",
                    VersionId = "mcC2LhSG",
                    ProjectName = "Just Enough Items",
                    DownloadUrl = "https://cdn.modrinth.com/data/u6dRKJwZ/versions/mcC2LhSG/jei.jar",
                    Sha1 = new string('1', 40),
                    Sha512 = new string('5', 128),
                });

                await db.SaveChangesAsync();

                var manifest = await new GetManifestQueryHandler(db).Handle(
                    new GetManifestQuery(serverId, "https://hopper.example.com"), CancellationToken.None);

                var json = JsonSerializer.Serialize(manifest);

                await Assert.That(json).IsEqualTo(
                    """{"mods":[{"file":"jei-1.20.1-15.2.0.27.jar","url":"https://hopper.example.com/api/blobs/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":1234567}]}""");

                await Assert.That(json).DoesNotContain("u6dRKJwZ");
                await Assert.That(json).DoesNotContain("cdn.modrinth.com");
                await Assert.That(json).DoesNotContain(new string('5', 128));
            }
        }

        private static ManifestDto WithModIds(params string[] ids) => new()
        {
            Mods =
            [
                new ManifestModDto
                {
                    File = "jei-1.20.1-15.3.0.4.jar",
                    Url = "https://hopper.example.com/api/blobs/" + new string('a', 64),
                    Sha256 = new string('a', 64),
                    Size = 1234567,
                    ModIds = ids,
                },
            ],
        };

        [Test]
        public async Task Serialize_EntryWithModIds_AppendsModIdsAfterSize()
        {
            var json = JsonSerializer.Serialize(WithModIds("jei"));

            await Assert.That(json).IsEqualTo(
                """{"mods":[{"file":"jei-1.20.1-15.3.0.4.jar","url":"https://hopper.example.com/api/blobs/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":1234567,"modIds":["jei"]}]}""");
        }

        [Test]
        public async Task Serialize_EntryWithSeveralModIds_EmitsThemAllInOrder()
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(WithModIds("embeddium", "rubidium")));
            var ids = document.RootElement.GetProperty("mods")[0].GetProperty("modIds");

            await Assert.That(ids.EnumerateArray().Select(e => e.GetString()!).ToList())
                .IsEquivalentTo(new[] { "embeddium", "rubidium" });
        }

        [Test]
        public async Task Serialize_EntryWithNullModIds_StillCarriesExactlyFourFields()
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(SampleManifest()));

            var names = document.RootElement.GetProperty("mods")[0].EnumerateObject().Select(p => p.Name).ToList();
            await Assert.That(names).IsEquivalentTo(new[] { "file", "url", "sha256", "size" });
        }

        [Test]
        public async Task Serialize_EntryWithAnEmptyModIdSet_StillCarriesExactlyFourFields()
        {
            var manifest = await ManifestFromRowAsync(modIds: []);

            using var document = JsonDocument.Parse(JsonSerializer.Serialize(manifest));
            var names = document.RootElement.GetProperty("mods")[0].EnumerateObject().Select(p => p.Name).ToList();

            await Assert.That(names).IsEquivalentTo(new[] { "file", "url", "sha256", "size" });
        }

        [Test]
        public async Task Serialize_RowWithModIds_CarriesThemThroughTheQueryHandler()
        {
            var manifest = await ManifestFromRowAsync(modIds: ["jei"]);

            await Assert.That(JsonSerializer.Serialize(manifest)).Contains("""
                "modIds":["jei"]
                """);
        }

        [Test]
        [Arguments("modIds")]
        public async Task Serialize_ModIds_SurvivesACamelCaseNamingPolicy(string expectedName)
        {
            var json = JsonSerializer.Serialize(
                WithModIds("jei"),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            using var document = JsonDocument.Parse(json);
            await Assert.That(document.RootElement.GetProperty("mods")[0].TryGetProperty(expectedName, out _)).IsTrue();
        }

        [Test]
        public async Task Serialize_ModIds_SurvivesASnakeCaseNamingPolicy()
        {
            var json = JsonSerializer.Serialize(
                WithModIds("jei"),
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower });

            using var document = JsonDocument.Parse(json);
            var entry = document.RootElement.GetProperty("mods")[0];

            await Assert.That(entry.TryGetProperty("modIds", out var ids)).IsTrue();
            await Assert.That(ids.EnumerateArray().Single().GetString()).IsEqualTo("jei");
        }

        [Test]
        public async Task Serialize_ModIds_IsAnArrayOfStrings()
        {
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(WithModIds("jei", "rubidium")));
            var ids = document.RootElement.GetProperty("mods")[0].GetProperty("modIds");

            await Assert.That(ids.ValueKind).IsEqualTo(JsonValueKind.Array);
            await Assert.That(ids.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String)).IsTrue();
        }

        private static async Task<ManifestDto> ManifestFromRowAsync(string[]? modIds)
        {
            var db = new HopperDbContext(new DbContextOptionsBuilder<HopperDbContext>()
                .UseInMemoryDatabase($"hopper-wire-{Guid.NewGuid():N}")
                .Options);

            await using (db)
            {
                var serverId = Guid.NewGuid();

                db.Mods.Add(new Mod
                {
                    ServerId = serverId,
                    FileName = "jei-1.20.1-15.3.0.4.jar",
                    Sha256 = new string('a', 64),
                    Size = 1234567,
                    ModIds = modIds,
                });

                await db.SaveChangesAsync();

                return await new GetManifestQueryHandler(db).Handle(
                    new GetManifestQuery(serverId, "https://hopper.example.com"), CancellationToken.None);
            }
        }

        [Test]
        public async Task Serialize_EmptyModSet_StillEmitsAModsArray()
        {
            var json = JsonSerializer.Serialize(new ManifestDto { Mods = [] });

            await Assert.That(json).IsEqualTo("""{"mods":[]}""");
        }
    }
}
