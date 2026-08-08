using HOPPER.Application;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Application.Modrinth
{
    public class ModrinthClient(IHttpClientFactory factory, IMemoryCache cache, IConfiguration configuration) : IModrinthClient
    {
        private static readonly JsonSerializerOptions Json = ModrinthJson.Options;

        private static readonly string[] DefaultDownloadHosts = ["cdn.modrinth.com"];

        private string[] DownloadHosts =>
            configuration.GetSection("Hopper:ModrinthDownloadHosts").Get<string[]>() is { Length: > 0 } configured
                ? configured
                : DefaultDownloadHosts;

        public async Task<ModrinthSearchResponse> SearchAsync(
            string? query,
            string? loader,
            string? gameVersion,
            ModrinthSearchIndex index,
            int offset,
            int limit,
            CancellationToken cancellationToken)
        {
            var parameters = new List<string>
            {
                $"index={index.ToApiValue()}",
                $"offset={ModrinthFacets.ClampOffset(offset)}",
                $"limit={ModrinthFacets.ClampLimit(limit)}",
                $"facets={Uri.EscapeDataString(ModrinthFacets.Build(loader, gameVersion))}",
            };

            if (!string.IsNullOrWhiteSpace(query))
                parameters.Add($"query={Uri.EscapeDataString(query.Trim())}");

            return await GetAsync<ModrinthSearchResponse>($"search?{string.Join('&', parameters)}", null, cancellationToken)
                ?? new ModrinthSearchResponse();
        }

        public async Task<ModrinthProject> GetProjectAsync(string idOrSlug, CancellationToken cancellationToken)
        {
            var id = RequireIdOrSlug(idOrSlug);

            if (cache.TryGetValue<ModrinthProject>(ProjectKey(id), out var cached) && cached is not null)
                return cached;

            var project = await GetAsync<ModrinthProject>($"project/{Uri.EscapeDataString(id)}", id, cancellationToken)
                ?? throw new ModrinthProjectNotFoundException(id);

            CacheProject(project);
            return project;
        }

        public async Task<IReadOnlyList<ModrinthProject>> GetProjectsAsync(
            IReadOnlyCollection<string> idsOrSlugs, CancellationToken cancellationToken)
        {
            var wanted = idsOrSlugs
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (wanted.Count == 0)
                return [];

            var resolved = new List<ModrinthProject>(wanted.Count);
            var missing = new List<string>(wanted.Count);

            foreach (var id in wanted)
            {
                if (cache.TryGetValue<ModrinthProject>(ProjectKey(id), out var cached) && cached is not null)
                    resolved.Add(cached);
                else
                    missing.Add(id);
            }

            if (missing.Count > 0)
            {
                var url = $"projects?ids={Uri.EscapeDataString(ModrinthFacets.JsonArray(missing))}";
                var fetched = await GetAsync<List<ModrinthProject>>(url, null, cancellationToken) ?? [];

                foreach (var project in fetched)
                {
                    CacheProject(project);
                    resolved.Add(project);
                }
            }

            return resolved;
        }

        public async Task<IReadOnlyList<ModrinthVersion>> ListVersionsAsync(
            string projectIdOrSlug,
            string? loader,
            string? gameVersion,
            bool includeChangelog,
            CancellationToken cancellationToken)
        {
            var id = RequireIdOrSlug(projectIdOrSlug);

            var parameters = new List<string>();
            if (!string.IsNullOrWhiteSpace(loader))
            {
                var validated = ModrinthFacets.ValidateLoader(loader);
                parameters.Add($"loaders={Uri.EscapeDataString(ModrinthFacets.JsonArray([validated]))}");
            }

            if (!string.IsNullOrWhiteSpace(gameVersion))
            {
                var validated = ModrinthFacets.ValidateGameVersion(gameVersion);
                parameters.Add($"game_versions={Uri.EscapeDataString(ModrinthFacets.JsonArray([validated]))}");
            }

            if (!includeChangelog)
                parameters.Add("include_changelog=false");

            var key = $"modrinth:versions:{id}:{loader}:{gameVersion}:{includeChangelog}";
            if (cache.TryGetValue<IReadOnlyList<ModrinthVersion>>(key, out var cached) && cached is not null)
                return cached;

            var query = parameters.Count == 0 ? string.Empty : "?" + string.Join('&', parameters);
            var versions = await GetAsync<List<ModrinthVersion>>(
                $"project/{Uri.EscapeDataString(id)}/version{query}", id, cancellationToken) ?? [];

            cache.Set<IReadOnlyList<ModrinthVersion>>(key, versions, TimeSpan.FromMinutes(2));
            return versions;
        }

        public async Task<ModrinthVersion> GetVersionAsync(string versionId, CancellationToken cancellationToken)
        {
            var id = RequireIdOrSlug(versionId);
            return await GetAsync<ModrinthVersion>($"version/{Uri.EscapeDataString(id)}", id, cancellationToken)
                ?? throw new ModrinthProjectNotFoundException(id);
        }

        public async Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(
            IReadOnlyCollection<string> versionIds, CancellationToken cancellationToken)
        {
            var wanted = versionIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (wanted.Count == 0)
                return [];

            var url = $"versions?ids={Uri.EscapeDataString(ModrinthFacets.JsonArray(wanted))}";
            return await GetAsync<List<ModrinthVersion>>(url, null, cancellationToken) ?? [];
        }

        public async Task<ModrinthTags> GetTagsAsync(CancellationToken cancellationToken)
        {
            const string key = "modrinth:tags";
            if (cache.TryGetValue<ModrinthTags>(key, out var cached) && cached is not null)
                return cached;

            var loaders = await GetAsync<List<ModrinthLoaderTag>>("tag/loader", null, cancellationToken) ?? [];
            var gameVersions = await GetAsync<List<ModrinthGameVersionTag>>("tag/game_version", null, cancellationToken) ?? [];

            var tags = new ModrinthTags(loaders, gameVersions);

            cache.Set(key, tags, TimeSpan.FromHours(6));
            return tags;
        }

        public async Task<Stream> OpenDownloadAsync(Uri url, CancellationToken cancellationToken)
        {
            if (!url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps)
                throw new InvalidRequestException($"Refusing to download over {url.Scheme}: {url}");

            if (!DownloadHosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase))
                throw new InvalidRequestException($"Refusing to download from {url.Host}. Only {string.Join(", ", DownloadHosts)} are allowed.");

            var http = factory.CreateClient(ModrinthHttpClients.Modrinth);
            var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                response.Dispose();
                throw new ModrinthApiException($"Downloading {url} failed with {(int)response.StatusCode} {response.StatusCode}.");
            }

            return new ResponseStream(response, await response.Content.ReadAsStreamAsync(cancellationToken));
        }

        private void CacheProject(ModrinthProject project)
        {
            if (!string.IsNullOrWhiteSpace(project.Id))
                cache.Set(ProjectKey(project.Id), project, TimeSpan.FromMinutes(5));

            if (!string.IsNullOrWhiteSpace(project.Slug))
                cache.Set(ProjectKey(project.Slug), project, TimeSpan.FromMinutes(5));
        }

        private static string ProjectKey(string idOrSlug) => $"modrinth:project:{idOrSlug}";

        private static string RequireIdOrSlug(string? value)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed))
                throw new InvalidRequestException("A Modrinth id or slug is required.");

            foreach (var c in trimmed)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_' && c != '.')
                    throw new InvalidRequestException($"Not a Modrinth id or slug: {value}");
            }

            return trimmed;
        }

        private async Task<T?> GetAsync<T>(string relativeUrl, string? subject, CancellationToken cancellationToken)
        {
            var http = factory.CreateClient(ModrinthHttpClients.Modrinth);

            HttpResponseMessage response;
            try
            {
                response = await http.GetAsync(relativeUrl, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                throw new ModrinthApiException($"Modrinth could not be reached: {ex.Message}");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new ModrinthApiException("Modrinth did not answer in time.");
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new ModrinthProjectNotFoundException(subject ?? relativeUrl);

                if (!response.IsSuccessStatusCode)
                    throw new ModrinthApiException(response.StatusCode, await DescriptionAsync(response, cancellationToken));

                try
                {
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    return await JsonSerializer.DeserializeAsync<T>(stream, Json, cancellationToken);
                }
                catch (JsonException ex)
                {
                    throw new ModrinthApiException($"Modrinth returned a response HOPPER could not read: {ex.Message}");
                }
            }
        }

        private static async Task<string?> DescriptionAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(body))
                    return null;

                using var document = JsonDocument.Parse(body);
                return document.RootElement.TryGetProperty("description", out var description)
                    ? description.GetString()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private sealed class ResponseStream(HttpResponseMessage response, Stream inner) : Stream
        {
            public override bool CanRead => inner.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush() => inner.Flush();

            public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
                inner.ReadAsync(buffer, cancellationToken);

            public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
                inner.ReadAsync(buffer, offset, count, cancellationToken);

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    inner.Dispose();
                    response.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }

    public static class ModrinthHttpClients
    {
        public const string Modrinth = "modrinth";
    }
}
