using System.Text.Json;
using HOPPER.Application.Modrinth;

namespace HOPPER.Tests.Modrinth
{
    public class ModrinthFacetTests
    {
        [Test]
        public async Task Build_Facets_IsAnArrayOfArrays()
        {
            var json = ModrinthFacets.Build("forge", "1.20.1");

            using var document = JsonDocument.Parse(json);
            await Assert.That(document.RootElement.ValueKind).IsEqualTo(JsonValueKind.Array);

            foreach (var inner in document.RootElement.EnumerateArray())
                await Assert.That(inner.ValueKind).IsEqualTo(JsonValueKind.Array);
        }

        [Test]
        public async Task Build_Loader_GoesInAsACategoryNotALoader()
        {
            var json = ModrinthFacets.Build("forge", null);

            await Assert.That(json).Contains("categories:forge");
            await Assert.That(json).DoesNotContain("loaders:");
        }

        [Test]
        public async Task Build_AlwaysConstrainsToMods()
        {
            await Assert.That(ModrinthFacets.Build(null, null)).Contains("project_type:mod");
        }

        [Test]
        public async Task Build_GameVersion_UsesTheVersionsFacet()
        {
            await Assert.That(ModrinthFacets.Build(null, "1.20.1")).Contains("versions:1.20.1");
        }

        [Test]
        public async Task Build_NoFilters_IsJustTheProjectType()
        {
            using var document = JsonDocument.Parse(ModrinthFacets.Build(null, null));
            await Assert.That(document.RootElement.GetArrayLength()).IsEqualTo(1);
        }

        [Test]
        public async Task ValidateLoader_SomethingModrinthDoesNotKnow_IsRefusedHere()
        {
            await Assert.That(() => ModrinthFacets.ValidateLoader("forgee")).Throws<ArgumentException>();
            await Assert.That(() => ModrinthFacets.ValidateLoader("")).Throws<ArgumentException>();
        }

        [Test]
        public async Task ValidateLoader_KnownLoaders_AreAcceptedAndLowercased()
        {
            await Assert.That(ModrinthFacets.ValidateLoader("Forge")).IsEqualTo("forge");
            await Assert.That(ModrinthFacets.ValidateLoader("neoforge")).IsEqualTo("neoforge");
            await Assert.That(ModrinthFacets.ValidateLoader("fabric")).IsEqualTo("fabric");
            await Assert.That(ModrinthFacets.ValidateLoader("quilt")).IsEqualTo("quilt");
        }

        [Test]
        public async Task ValidateGameVersion_RealMinecraftVersions_AllPass()
        {
            await Assert.That(ModrinthFacets.ValidateGameVersion("1.20.1")).IsEqualTo("1.20.1");
            await Assert.That(ModrinthFacets.ValidateGameVersion("23w13a_or_b")).IsEqualTo("23w13a_or_b");
            await Assert.That(ModrinthFacets.ValidateGameVersion("1.21.1-rc1")).IsEqualTo("1.21.1-rc1");
        }

        [Test]
        public async Task ValidateGameVersion_SomethingThatWouldEscapeAQueryString_IsRefused()
        {
            await Assert.That(() => ModrinthFacets.ValidateGameVersion("1.20.1\"],[\"x")).Throws<ArgumentException>();
            await Assert.That(() => ModrinthFacets.ValidateGameVersion(new string('9', 40))).Throws<ArgumentException>();
        }

        [Test]
        public async Task ClampLimit_IsCappedAtAHundredOnThisSide()
        {
            await Assert.That(ModrinthFacets.ClampLimit(500)).IsEqualTo(100);
            await Assert.That(ModrinthFacets.ClampLimit(0)).IsEqualTo(1);
            await Assert.That(ModrinthFacets.ClampLimit(-3)).IsEqualTo(1);
            await Assert.That(ModrinthFacets.ClampLimit(20)).IsEqualTo(20);
        }

        [Test]
        public async Task ClampOffset_NeverGoesNegative()
        {
            await Assert.That(ModrinthFacets.ClampOffset(-1)).IsEqualTo(0);
            await Assert.That(ModrinthFacets.ClampOffset(250)).IsEqualTo(250);
        }

        [Test]
        public async Task ToApiValue_EverySortOrder_HasAValueTheApiAccepts()
        {
            await Assert.That(ModrinthSearchIndex.Relevance.ToApiValue()).IsEqualTo("relevance");
            await Assert.That(ModrinthSearchIndex.Downloads.ToApiValue()).IsEqualTo("downloads");
            await Assert.That(ModrinthSearchIndex.Follows.ToApiValue()).IsEqualTo("follows");
            await Assert.That(ModrinthSearchIndex.Newest.ToApiValue()).IsEqualTo("newest");
            await Assert.That(ModrinthSearchIndex.Updated.ToApiValue()).IsEqualTo("updated");
            await Assert.That(() => ((ModrinthSearchIndex)99).ToApiValue()).Throws<ArgumentException>();
        }

        [Test]
        public async Task JsonArray_ProducesTheEncodingLoadersAndIdsNeed()
        {
            await Assert.That(ModrinthFacets.JsonArray(["forge"])).IsEqualTo("[\"forge\"]");
            await Assert.That(ModrinthFacets.JsonArray(["a", "b"])).IsEqualTo("[\"a\",\"b\"]");
        }
    }
}
