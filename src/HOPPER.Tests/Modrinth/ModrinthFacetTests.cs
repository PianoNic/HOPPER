using System.Text.Json;
using HOPPER.Application.Modrinth;

namespace HOPPER.Tests.Modrinth
{
    /// <summary>
    /// Three of Modrinth's search behaviours fail as "wrong results" rather than as an exception, and
    /// all three are the kind of bug that gets reported as "the browser finds nothing":
    ///
    ///  * an unknown facet name returns HTTP 200 with zero hits, not an error;
    ///  * limit clamps at 100 silently and the response echoes the clamped value;
    ///  * a flat facets array is a 400, but a bare loaders= string on the version endpoint is
    ///    silently IGNORED and the whole unfiltered list comes back.
    ///
    /// So the validation has to happen on HOPPER's side, before a request exists, and that is what
    /// these assert.
    /// </summary>
    public class ModrinthFacetTests
    {
        [Test]
        public async Task Build_Facets_IsAnArrayOfArrays()
        {
            // A flat array is a hard 400 from the API: "invalid type: string, expected a sequence".
            var json = ModrinthFacets.Build("forge", "1.20.1");

            using var document = JsonDocument.Parse(json);
            await Assert.That(document.RootElement.ValueKind).IsEqualTo(JsonValueKind.Array);

            foreach (var inner in document.RootElement.EnumerateArray())
                await Assert.That(inner.ValueKind).IsEqualTo(JsonValueKind.Array);
        }

        [Test]
        public async Task Build_Loader_GoesInAsACategoryNotALoader()
        {
            // The search endpoint folds loaders into categories and says so in its own schema. The
            // undocumented loaders: facet also works there and returns a slightly different set, which
            // is exactly why it is not used - one documented contract, not two.
            var json = ModrinthFacets.Build("forge", null);

            await Assert.That(json).Contains("categories:forge");
            await Assert.That(json).DoesNotContain("loaders:");
        }

        [Test]
        public async Task Build_AlwaysConstrainsToMods()
        {
            // HOPPER distributes jars into a mods directory. A resource pack or a shader in the result
            // list is an entry the client cannot use.
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
            // Forwarding it would produce 200 with zero hits, which is indistinguishable from "no mods
            // match" and would be reported as a missing mod rather than as a typo.
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
            // Minecraft versions are not semver. All three of these are real and a shape check would
            // reject two of them.
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
            // The API clamps at 100 and echoes the clamped value rather than saying so, so a caller
            // asking for 500 silently gets 100. Clamping here means the number sent is the number
            // meant.
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
            // An index outside their set is a hard 400, so a validated enum is the only safe way to
            // carry a sort order across the boundary.
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
            // A bare string in loaders= is not rejected by the API - it is ignored, and the entire
            // filter silently disappears. This encoding is why the client never lets a call site build
            // that parameter itself.
            await Assert.That(ModrinthFacets.JsonArray(["forge"])).IsEqualTo("[\"forge\"]");
            await Assert.That(ModrinthFacets.JsonArray(["a", "b"])).IsEqualTo("[\"a\",\"b\"]");
        }
    }
}
