using System.Text.Json;

namespace HOPPER.Application.Modrinth
{
    public enum ModrinthSearchIndex
    {
        Relevance = 0,
        Downloads = 1,
        Follows = 2,
        Newest = 3,
        Updated = 4,
    }

    public static class ModrinthFacets
    {
        public const int MaxLimit = 100;
        public const int MaxOffset = 5000;

        public static readonly IReadOnlySet<string> KnownLoaders =
            new HashSet<string>(StringComparer.Ordinal) { "forge", "neoforge", "fabric", "quilt" };

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

        public static string JsonArray(IEnumerable<string> values) => JsonSerializer.Serialize(values.ToArray());
    }
}
