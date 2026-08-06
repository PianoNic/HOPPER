using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Application.Imports
{
    /// <summary>One resolved CurseForge file. <see cref="DownloadUrl"/> is null exactly when the
    /// author set allowModDistribution=false - the genuine "blocked mod" - which is the case that
    /// stays pending even with a key.</summary>
    public sealed record CurseForgeFile(
        int ProjectId,
        int FileId,
        string? FileName,
        Uri? DownloadUrl,
        long? Length,
        string? Sha1,
        string? DisplayName);

    public interface ICurseForgeClient
    {
        /// <summary>False when no CurseForge:ApiKey is configured, which is the shipped default.
        /// HOPPER neither hardcodes nor bundles a key, so a stock install resolves nothing and every
        /// manifest entry becomes a pending row for the admin to satisfy by hand.</summary>
        bool IsConfigured { get; }

        Task<IReadOnlyDictionary<int, CurseForgeFile>> ResolveAsync(
            IReadOnlyList<int> fileIds, CancellationToken cancellationToken);

        /// <summary>Prism's fallback for a blocked file: the same jar is often published on Modrinth,
        /// and Modrinth's lookup-by-hash endpoint needs no auth. It still needs the CurseForge key
        /// first, because the sha1 it queries by only comes from the CurseForge API.</summary>
        Task<Uri?> FindOnModrinthBySha1Async(string sha1, CancellationToken cancellationToken);
    }

    public class CurseForgeClient(IHttpClientFactory factory, IConfiguration configuration) : ICurseForgeClient
    {
        /// <summary>CurseForge caps a batch at this many ids per request.</summary>
        private const int BatchSize = 100;

        private string? ApiKey => configuration["CurseForge:ApiKey"] is { Length: > 0 } key ? key : null;

        public bool IsConfigured => ApiKey is not null;

        public async Task<IReadOnlyDictionary<int, CurseForgeFile>> ResolveAsync(
            IReadOnlyList<int> fileIds, CancellationToken cancellationToken)
        {
            var key = ApiKey;
            if (key is null || fileIds.Count == 0)
                return new Dictionary<int, CurseForgeFile>();

            var resolved = new Dictionary<int, CurseForgeFile>();
            using var http = factory.CreateClient(ImportHttpClients.Packs);

            foreach (var batch in fileIds.Distinct().Chunk(BatchSize))
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.curseforge.com/v1/mods/files")
                {
                    Content = JsonContent.Create(new { fileIds = batch }),
                };
                request.Headers.Add("x-api-key", key);

                using var response = await http.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    // A bad key or a rate limit is not a reason to fail the import: every unresolved
                    // entry simply stays pending, which is exactly the keyless behaviour.
                    continue;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var file in data.EnumerateArray())
                {
                    if (!file.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.Number)
                        continue;

                    var fileId = id.GetInt32();
                    var modId = file.TryGetProperty("modId", out var m) && m.ValueKind == JsonValueKind.Number
                        ? m.GetInt32()
                        : 0;

                    // Null, absent, or empty all mean the same thing here: the author disabled
                    // third-party distribution and there is nothing to fetch.
                    var rawUrl = file.TryGetProperty("downloadUrl", out var u) && u.ValueKind == JsonValueKind.String
                        ? u.GetString()
                        : null;

                    resolved[fileId] = new CurseForgeFile(
                        ProjectId: modId,
                        FileId: fileId,
                        FileName: file.TryGetProperty("fileName", out var n) ? n.GetString() : null,
                        DownloadUrl: Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ? uri : null,
                        Length: file.TryGetProperty("fileLength", out var l) && l.ValueKind == JsonValueKind.Number
                            ? l.GetInt64()
                            : null,
                        Sha1: Sha1Of(file),
                        DisplayName: file.TryGetProperty("displayName", out var d) ? d.GetString() : null);
                }
            }

            return resolved;
        }

        public async Task<Uri?> FindOnModrinthBySha1Async(string sha1, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sha1))
                return null;

            try
            {
                using var http = factory.CreateClient(ImportHttpClients.Packs);
                using var response = await http.PostAsJsonAsync(
                    "https://api.modrinth.com/v2/version_files",
                    new { hashes = new[] { sha1 }, algorithm = "sha1" },
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                    return null;

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                // The response is keyed by the hash that was asked for.
                foreach (var version in document.RootElement.EnumerateObject())
                {
                    if (!version.Value.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
                        continue;

                    foreach (var file in files.EnumerateArray())
                    {
                        if (file.TryGetProperty("url", out var url)
                            && Uri.TryCreate(url.GetString(), UriKind.Absolute, out var uri))
                        {
                            return uri;
                        }
                    }
                }
            }
            catch (HttpRequestException)
            {
                // A best-effort recovery. Failing it just leaves the entry pending, which is where it
                // already was.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            return null;
        }

        /// <summary>hashes[] is [{value, algo}] with algo 1 = SHA1 and 2 = MD5. Note there is no
        /// SHA-256 anywhere in this API, which is why the blob address is always computed locally.</summary>
        private static string? Sha1Of(JsonElement file)
        {
            if (!file.TryGetProperty("hashes", out var hashes) || hashes.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var hash in hashes.EnumerateArray())
            {
                if (hash.TryGetProperty("algo", out var algo)
                    && algo.ValueKind == JsonValueKind.Number
                    && algo.GetInt32() == 1)
                {
                    return hash.TryGetProperty("value", out var value) ? value.GetString() : null;
                }
            }

            return null;
        }
    }

    public static class ImportHttpClients
    {
        /// <summary>Named client for everything the importer fetches: pack archives, mod jars, and the
        /// two APIs. One long timeout and one User-Agent, configured once in Program.cs.</summary>
        public const string Packs = "packs";
    }
}
