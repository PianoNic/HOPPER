using System.Text.Json;

namespace HOPPER.Application.Modrinth
{
    /// <summary>Sort order for /search. A validated enum rather than a string because anything outside
    /// this set is a hard HTTP 400 from Modrinth, and a 400 from an upstream is the least useful way
    /// to learn that a dashboard sent "date" instead of "updated".</summary>
    public enum ModrinthSearchIndex
    {
        Relevance = 0,
        Downloads = 1,
        Follows = 2,
        Newest = 3,
        Updated = 4,
    }

    /// <summary>Builds and validates the two things Modrinth's search silently mishandles.
    ///
    /// Three behaviours make this file necessary, and all three fail as "wrong results" rather than as
    /// an exception, which is the worst way for a bug to present:
    ///
    ///  * facets is a JSON ARRAY OF ARRAYS - inner array ORs, outer ANDs. A flat array is a 400.
    ///  * An UNKNOWN facet name is not an error. It returns 200 with zero hits, which is
    ///    indistinguishable from "no mods match". Facet names are therefore validated here, before a
    ///    request is built, rather than trusted to the API.
    ///  * limit clamps at 100 silently and echoes the clamped value, so a caller asking for 500 gets
    ///    100 and no indication that it happened. It is clamped on this side instead.
    ///
    /// Note also that loaders are filtered as CATEGORIES here. The search endpoint folds loaders into
    /// categories and says so in its own schema; the separate loaders: facet is undocumented and
    /// returns a slightly different set, so it is deliberately not used. On /project/{id}/version the
    /// loader is a real, separate parameter. Two endpoints, two vocabularies, and mixing them up
    /// produces a filter that quietly matches nothing.</summary>
    public static class ModrinthFacets
    {
        public const int MaxLimit = 100;
        public const int MaxOffset = 5000;

        /// <summary>The loader facet values HOPPER will send. Anything else is refused rather than
        /// forwarded, because Modrinth answers an unknown facet with an empty result set.</summary>
        public static readonly IReadOnlySet<string> KnownLoaders =
            new HashSet<string>(StringComparer.Ordinal) { "forge", "neoforge", "fabric", "quilt" };

        /// <summary>Builds the facets query value, already JSON but not yet URL-encoded.
        ///
        /// project_type:mod is always present - HOPPER distributes jars into a mods directory, and a
        /// resource pack or a shader in that list would be an entry the client cannot use.</summary>
        public static string Build(string? loader, string? gameVersion)
        {
            var facets = new List<string[]>();
            facets.Add(["project_type:mod"]);

            if (!string.IsNullOrWhiteSpace(loader))
                facets.Add([$"categories:{ValidateLoader(loader)}"]);

            if (!string.IsNullOrWhiteSpace(gameVersion))
                facets.Add([$"versions:{ValidateGameVersion(gameVersion)}"]);

            return JsonSerializer.Serialize(facets);
        }

        public static string ValidateLoader(string? loader)
        {
            var value = loader?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("A loader is required.");

            if (!KnownLoaders.Contains(value))
                throw new ArgumentException($"Unknown loader: {loader}. Modrinth answers an unknown filter with no results rather than an error, so it is refused here.");

            return value;
        }

        /// <summary>Minecraft version strings are not semver - "1.20.1", "23w13a_or_b" and "1.21.1-rc1"
        /// are all real - so this checks the character set and the length rather than a shape.</summary>
        public static string ValidateGameVersion(string? gameVersion)
        {
            var value = gameVersion?.Trim();
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("A Minecraft version is required.");

            if (value.Length > 32)
                throw new ArgumentException($"Not a Minecraft version: {gameVersion}");

            foreach (var c in value)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-')
                    throw new ArgumentException($"Not a Minecraft version: {gameVersion}");
            }

            return value;
        }

        public static int ClampLimit(int limit) => Math.Clamp(limit, 1, MaxLimit);

        public static int ClampOffset(int offset) => Math.Clamp(offset, 0, MaxOffset);

        public static string ToApiValue(this ModrinthSearchIndex index) => index switch
        {
            ModrinthSearchIndex.Relevance => "relevance",
            ModrinthSearchIndex.Downloads => "downloads",
            ModrinthSearchIndex.Follows => "follows",
            ModrinthSearchIndex.Newest => "newest",
            ModrinthSearchIndex.Updated => "updated",
            _ => throw new ArgumentException($"Unknown sort order: {index}. Modrinth rejects an unknown index outright."),
        };

        /// <summary>Encodes a list as the JSON array Modrinth expects for ids=, loaders= and
        /// game_versions=. A bare string there is not rejected - it is silently IGNORED and the whole
        /// filter disappears, so the encoding never happens at a call site.</summary>
        public static string JsonArray(IEnumerable<string> values) => JsonSerializer.Serialize(values.ToArray());
    }
}
