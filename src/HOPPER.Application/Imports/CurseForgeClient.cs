using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Application.Imports
{
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
        bool IsConfigured { get; }

        Task<IReadOnlyDictionary<int, CurseForgeFile>> ResolveAsync(
            IReadOnlyList<int> fileIds, CancellationToken cancellationToken);

        Task<Uri?> FindOnModrinthBySha1Async(string sha1, CancellationToken cancellationToken);
    }

    public class CurseForgeClient(IHttpClientFactory factory, IConfiguration configuration) : ICurseForgeClient
    {
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
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }

            return null;
        }

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
        public const string Packs = "packs";
    }
}
