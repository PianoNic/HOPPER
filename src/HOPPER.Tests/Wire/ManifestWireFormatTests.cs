using System.Text.Json;
using HOPPER.Application.Dtos.Manifest;
using HOPPER.Application.Queries.Manifest;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Tests.Wire
{
    /// <summary>
    /// The manifest is a fixed, already-shipped contract: a jar sitting in players' mods folders
    /// parses it with Gson, keyed on the literal strings "mods", "file", "url", "sha256" and "size".
    /// Those clients cannot be redeployed, so a rename here is not a compile error anywhere - it is
    /// a silent, permanent break in the field. These tests assert on raw JSON text rather than on a
    /// round-trip, because a round-trip through the same DTO would agree with itself no matter what
    /// the names became.
    /// </summary>
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
            // The [JsonPropertyName] attributes exist precisely so a global naming policy cannot move
            // these. Left to the policy, Sha256 would come out as "sha256" today but "shA256" after a
            // rename to SHA256 - which the client reads as a null hash and answers by re-downloading
            // every jar on every launch, forever, with nothing failing loudly.
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
            // Java's Entry.size is a primitive long. Gson fails the parse of the whole entry on a
            // quoted value, which takes the entire manifest down, not just this field.
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(SampleManifest()));
            var size = document.RootElement.GetProperty("mods")[0].GetProperty("size");

            await Assert.That(size.ValueKind).IsEqualTo(JsonValueKind.Number);
            await Assert.That(size.GetInt64()).IsEqualTo(1234567L);
        }

        [Test]
        public async Task Serialize_Root_CarriesModsAndNothingElse()
        {
            // No envelope: the Java client reads the root object's "mods" array directly, so an extra
            // wrapper or a sibling property is a contract change even though it parses.
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
            // Provenance is invisible to the client, by design. A mod added from the browser carries a
            // project id, a version id, a CDN URL and Modrinth's sha1/sha512; none of that belongs in
            // the manifest, and a client in the field would not know what to do with it. The one hash
            // that appears here is HOPPER's own sha256, which Modrinth never publishes and which the
            // installer computed from the bytes it stored.
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

                // Byte-identical to the hand-uploaded case above.
                await Assert.That(json).IsEqualTo(
                    """{"mods":[{"file":"jei-1.20.1-15.2.0.27.jar","url":"https://hopper.example.com/api/blobs/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","sha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","size":1234567}]}""");

                // And nothing from provenance leaked in under any name.
                await Assert.That(json).DoesNotContain("u6dRKJwZ");
                await Assert.That(json).DoesNotContain("cdn.modrinth.com");
                await Assert.That(json).DoesNotContain(new string('5', 128));
            }
        }

        [Test]
        public async Task Serialize_EmptyModSet_StillEmitsAModsArray()
        {
            // Syncer.fetchManifest() throws "manifest is empty or malformed" when mods is absent or
            // null, which fails the sync. An empty server-side set has to come out as [].
            var json = JsonSerializer.Serialize(new ManifestDto { Mods = [] });

            await Assert.That(json).IsEqualTo("""{"mods":[]}""");
        }
    }
}
