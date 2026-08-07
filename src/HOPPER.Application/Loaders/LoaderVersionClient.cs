using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using HOPPER.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace HOPPER.Application.Loaders
{
    /// <param name="Recommended">
    /// The build the loader's own maintainers point people at. For Forge that is a different thing
    /// from the newest build, and the distinction is the whole reason this exists: promotions_slim
    /// publishes `1.20.1-recommended` and `1.20.1-latest` separately and they routinely disagree.
    /// </param>
    public sealed record LoaderVersion(string Version, bool Recommended);

    public interface ILoaderVersionClient
    {
        Task<IReadOnlyList<LoaderVersion>> GetAsync(ModLoader loader, string? minecraftVersion, CancellationToken cancellationToken);
    }

    public sealed class LoaderVersionUnavailableException(ModLoader loader, Exception inner)
        : InvalidOperationException(
            $"Could not reach the {loader} version list. Type the build by hand, or try again once the network is back.",
            inner);

    public sealed class LoaderVersionClient(IHttpClientFactory factory, IMemoryCache cache) : ILoaderVersionClient
    {
        public const string HttpClientName = "hopper-loaders";

        // Long, because these lists move on the order of days and a dialog opens far more often
        // than a loader ships. Short enough that a fresh build shows up the same afternoon.
        private static readonly TimeSpan CacheFor = TimeSpan.FromHours(6);

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public async Task<IReadOnlyList<LoaderVersion>> GetAsync(
            ModLoader loader, string? minecraftVersion, CancellationToken cancellationToken)
        {
            var key = $"loader-versions:{loader}:{minecraftVersion}";
            if (cache.TryGetValue<IReadOnlyList<LoaderVersion>>(key, out var cached) && cached is not null)
                return cached;

            IReadOnlyList<LoaderVersion> versions;
            try
            {
                versions = loader switch
                {
                    ModLoader.Forge => await ForgeAsync(minecraftVersion, cancellationToken),
                    ModLoader.NeoForge => await NeoForgeAsync(minecraftVersion, cancellationToken),
                    ModLoader.Fabric => await FabricAsync(cancellationToken),
                    ModLoader.Quilt => await QuiltAsync(cancellationToken),
                    _ => [],
                };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or System.Xml.XmlException)
            {
                // Not cached: a network blip must not lock the dialog out of the list for six hours.
                throw new LoaderVersionUnavailableException(loader, ex);
            }

            cache.Set(key, versions, CacheFor);
            return versions;
        }

        // promotions_slim keys builds as "<mc>-recommended" and "<mc>-latest", and carries no other
        // list, so the recommended build is all this can offer for a given Minecraft version.
        private async Task<IReadOnlyList<LoaderVersion>> ForgeAsync(string? minecraftVersion, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(minecraftVersion)) return [];

            var promos = await GetJsonAsync<ForgePromotions>(
                "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json", cancellationToken);

            var recommended = promos?.Promos?.GetValueOrDefault($"{minecraftVersion}-recommended");
            var latest = promos?.Promos?.GetValueOrDefault($"{minecraftVersion}-latest");

            var versions = new List<LoaderVersion>();
            if (!string.IsNullOrWhiteSpace(recommended)) versions.Add(new LoaderVersion(recommended, true));
            if (!string.IsNullOrWhiteSpace(latest) && latest != recommended) versions.Add(new LoaderVersion(latest, false));

            return versions;
        }

        // NeoForge encodes the Minecraft version in the build: 21.1.x is Minecraft 1.21.1. There is
        // no recommended flag, so the newest stable build is what gets marked.
        private async Task<IReadOnlyList<LoaderVersion>> NeoForgeAsync(string? minecraftVersion, CancellationToken cancellationToken)
        {
            var xml = await GetStringAsync(
                "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml", cancellationToken);

            var all = XDocument.Parse(xml)
                .Descendants("version")
                .Select(v => v.Value)
                .Where(IsStable)
                .Reverse()
                .ToList();

            var prefix = NeoForgePrefix(minecraftVersion);
            var matching = prefix is null
                ? all
                : all.Where(v => v.StartsWith(prefix, StringComparison.Ordinal)).ToList();

            return Mark(matching.Count > 0 ? matching : all);
        }

        /// <summary>1.21.1 to "21.1.", and 1.21 to "21.0." - NeoForge's own scheme.</summary>
        public static string? NeoForgePrefix(string? minecraftVersion)
        {
            if (string.IsNullOrWhiteSpace(minecraftVersion)) return null;

            var parts = minecraftVersion.Split('.');
            if (parts.Length < 2 || parts[0] != "1") return null;

            var patch = parts.Length > 2 ? parts[2] : "0";
            return $"{parts[1]}.{patch}.";
        }

        private async Task<IReadOnlyList<LoaderVersion>> FabricAsync(CancellationToken cancellationToken)
        {
            var entries = await GetJsonAsync<List<FabricLoader>>(
                "https://meta.fabricmc.net/v2/versions/loader", cancellationToken) ?? [];

            var stable = entries.FirstOrDefault(e => e.Stable)?.Version;
            return entries
                .Select(e => new LoaderVersion(e.Version, e.Version == stable))
                .ToList();
        }

        private async Task<IReadOnlyList<LoaderVersion>> QuiltAsync(CancellationToken cancellationToken)
        {
            var entries = await GetJsonAsync<List<QuiltLoader>>(
                "https://meta.quiltmc.org/v3/versions/loader", cancellationToken) ?? [];

            // Quilt publishes no stable flag and its newest entry is routinely a beta, so the
            // prereleases go: recommending one would be worse than offering a slightly older build.
            return Mark(entries.Select(e => e.Version).Where(IsStable).ToList());
        }

        /// <summary>Anything carrying a prerelease marker, whatever the loader calls it.</summary>
        public static bool IsStable(string version) =>
            !version.Contains("beta", StringComparison.OrdinalIgnoreCase)
            && !version.Contains("alpha", StringComparison.OrdinalIgnoreCase)
            && !version.Contains("-rc", StringComparison.OrdinalIgnoreCase)
            && !version.Contains("pre", StringComparison.OrdinalIgnoreCase);

        private static IReadOnlyList<LoaderVersion> Mark(IReadOnlyList<string> newestFirst) =>
            newestFirst.Select((v, i) => new LoaderVersion(v, i == 0)).ToList();

        private async Task<T?> GetJsonAsync<T>(string url, CancellationToken cancellationToken)
        {
            using var response = await factory.CreateClient(HttpClientName).GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync<T>(stream, Json, cancellationToken);
        }

        private async Task<string> GetStringAsync(string url, CancellationToken cancellationToken)
        {
            using var response = await factory.CreateClient(HttpClientName).GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }

        private sealed record ForgePromotions
        {
            [JsonPropertyName("promos")]
            public Dictionary<string, string>? Promos { get; init; }
        }

        private sealed record FabricLoader
        {
            [JsonPropertyName("version")]
            public string Version { get; init; } = string.Empty;

            [JsonPropertyName("stable")]
            public bool Stable { get; init; }
        }

        private sealed record QuiltLoader
        {
            [JsonPropertyName("version")]
            public string Version { get; init; } = string.Empty;
        }
    }
}
