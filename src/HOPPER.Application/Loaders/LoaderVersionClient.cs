using HOPPER.Domain;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using HOPPER.Domain.Enums;
using Microsoft.Extensions.Caching.Memory;

namespace HOPPER.Application.Loaders
{
    public sealed record LoaderVersion(string Version, bool Recommended);

    public interface ILoaderVersionClient
    {
        Task<IReadOnlyList<LoaderVersion>> GetAsync(ModLoader loader, string? minecraftVersion, CancellationToken cancellationToken);
    }

    public sealed class LoaderVersionsNotConfiguredException(ModLoader loader)
        : RuleViolationException(
            $"HOPPER has no version source for {loader}, so it cannot offer a list. Type the build by hand.");

    public sealed class LoaderVersionUnavailableException(ModLoader loader, Exception inner)
        : InvalidOperationException(
            $"Could not reach the {loader} version list. Type the build by hand, or try again once the network is back.",
            inner);

    public sealed class LoaderVersionClient(IHttpClientFactory factory, IMemoryCache cache) : ILoaderVersionClient
    {
        public const string HttpClientName = "hopper-loaders";

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

                    // Not an empty list. A loader with no version source here answered 200 with
                    // nothing in it, so the dropdown was blank and looked like an upstream outage
                    // rather than a loader that was never wired up.
                    _ => throw new LoaderVersionsNotConfiguredException(loader),
                };
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or System.Xml.XmlException)
            {
                throw new LoaderVersionUnavailableException(loader, ex);
            }

            cache.Set(key, versions, CacheFor);
            return versions;
        }

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

            return Mark(entries.Select(e => e.Version).Where(IsStable).ToList());
        }

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
