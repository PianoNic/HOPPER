using System.Text.Json;
using System.Text.Json.Serialization;

namespace HOPPER.Application.Modrinth
{
    public static class ModrinthJson
    {
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
    }

    public sealed record ModrinthSearchResponse
    {
        private readonly IReadOnlyList<ModrinthHit> _hits = [];

        [JsonPropertyName("hits")]
        public IReadOnlyList<ModrinthHit> Hits { get => _hits; init => _hits = value ?? []; }

        [JsonPropertyName("offset")]
        public int Offset { get; init; }

        [JsonPropertyName("limit")]
        public int Limit { get; init; }

        [JsonPropertyName("total_hits")]
        public int TotalHits { get; init; }
    }

    public sealed record ModrinthHit
    {
        private readonly IReadOnlyList<string> _categories = [];
        private readonly IReadOnlyList<string> _versions = [];

        [JsonPropertyName("project_id")]
        public string ProjectId { get; init; } = string.Empty;

        [JsonPropertyName("slug")]
        public string? Slug { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("author")]
        public string? Author { get; init; }

        [JsonPropertyName("categories")]
        public IReadOnlyList<string> Categories { get => _categories; init => _categories = value ?? []; }

        [JsonPropertyName("versions")]
        public IReadOnlyList<string> Versions { get => _versions; init => _versions = value ?? []; }

        [JsonPropertyName("downloads")]
        public long Downloads { get; init; }

        [JsonPropertyName("follows")]
        public long Follows { get; init; }

        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; init; }

        [JsonPropertyName("client_side")]
        public string? ClientSide { get; init; }

        [JsonPropertyName("server_side")]
        public string? ServerSide { get; init; }

        [JsonPropertyName("project_type")]
        public string? ProjectType { get; init; }

        [JsonPropertyName("latest_version")]
        public string? LatestVersion { get; init; }

        [JsonPropertyName("date_modified")]
        public DateTimeOffset? DateModified { get; init; }
    }

    public sealed record ModrinthProject
    {
        private readonly IReadOnlyList<string> _categories = [];
        private readonly IReadOnlyList<string> _loaders = [];
        private readonly IReadOnlyList<string> _gameVersions = [];
        private readonly IReadOnlyList<string> _versions = [];

        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("slug")]
        public string? Slug { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("body")]
        public string? Body { get; init; }

        [JsonPropertyName("categories")]
        public IReadOnlyList<string> Categories { get => _categories; init => _categories = value ?? []; }

        [JsonPropertyName("loaders")]
        public IReadOnlyList<string> Loaders { get => _loaders; init => _loaders = value ?? []; }

        [JsonPropertyName("game_versions")]
        public IReadOnlyList<string> GameVersions { get => _gameVersions; init => _gameVersions = value ?? []; }

        [JsonPropertyName("versions")]
        public IReadOnlyList<string> Versions { get => _versions; init => _versions = value ?? []; }

        [JsonPropertyName("downloads")]
        public long Downloads { get; init; }

        [JsonPropertyName("followers")]
        public long Followers { get; init; }

        [JsonPropertyName("icon_url")]
        public string? IconUrl { get; init; }

        [JsonPropertyName("source_url")]
        public string? SourceUrl { get; init; }

        [JsonPropertyName("issues_url")]
        public string? IssuesUrl { get; init; }

        [JsonPropertyName("client_side")]
        public string? ClientSide { get; init; }

        [JsonPropertyName("server_side")]
        public string? ServerSide { get; init; }

        [JsonPropertyName("project_type")]
        public string? ProjectType { get; init; }
    }

    public sealed record ModrinthVersion
    {
        private readonly IReadOnlyList<string> _gameVersions = [];
        private readonly IReadOnlyList<string> _loaders = [];
        private readonly IReadOnlyList<ModrinthVersionFile> _files = [];
        private readonly IReadOnlyList<ModrinthDependency> _dependencies = [];

        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("project_id")]
        public string ProjectId { get; init; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("version_number")]
        public string? VersionNumber { get; init; }

        [JsonPropertyName("version_type")]
        public string? VersionType { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("date_published")]
        public DateTimeOffset? DatePublished { get; init; }

        [JsonPropertyName("downloads")]
        public long Downloads { get; init; }

        [JsonPropertyName("featured")]
        public bool Featured { get; init; }

        [JsonPropertyName("game_versions")]
        public IReadOnlyList<string> GameVersions { get => _gameVersions; init => _gameVersions = value ?? []; }

        [JsonPropertyName("loaders")]
        public IReadOnlyList<string> Loaders { get => _loaders; init => _loaders = value ?? []; }

        [JsonPropertyName("files")]
        public IReadOnlyList<ModrinthVersionFile> Files { get => _files; init => _files = value ?? []; }

        [JsonPropertyName("dependencies")]
        public IReadOnlyList<ModrinthDependency> Dependencies { get => _dependencies; init => _dependencies = value ?? []; }

        [JsonPropertyName("changelog")]
        public string? Changelog { get; init; }
    }

    public sealed record ModrinthVersionFile
    {
        private readonly IReadOnlyDictionary<string, string> _hashes = new Dictionary<string, string>();

        [JsonPropertyName("hashes")]
        public IReadOnlyDictionary<string, string> Hashes
        {
            get => _hashes;
            init => _hashes = value ?? new Dictionary<string, string>();
        }

        [JsonPropertyName("url")]
        public string? Url { get; init; }

        [JsonPropertyName("filename")]
        public string? FileName { get; init; }

        [JsonPropertyName("primary")]
        public bool Primary { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }

        [JsonPropertyName("file_type")]
        public string? FileType { get; init; }

        public string? Sha1 => Hashes.TryGetValue("sha1", out var value) ? value : null;

        public string? Sha512 => Hashes.TryGetValue("sha512", out var value) ? value : null;
    }

    public sealed record ModrinthDependency
    {
        [JsonPropertyName("version_id")]
        public string? VersionId { get; init; }

        [JsonPropertyName("project_id")]
        public string? ProjectId { get; init; }

        [JsonPropertyName("file_name")]
        public string? FileName { get; init; }

        [JsonPropertyName("dependency_type")]
        public string? DependencyType { get; init; }
    }

    public sealed record ModrinthGameVersionTag
    {
        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;

        [JsonPropertyName("version_type")]
        public string? VersionType { get; init; }

        [JsonPropertyName("major")]
        public bool Major { get; init; }
    }

    public sealed record ModrinthLoaderTag
    {
        private readonly IReadOnlyList<string> _supportedProjectTypes = [];

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("supported_project_types")]
        public IReadOnlyList<string> SupportedProjectTypes
        {
            get => _supportedProjectTypes;
            init => _supportedProjectTypes = value ?? [];
        }
    }

    public sealed record ModrinthTags(
        IReadOnlyList<ModrinthLoaderTag> Loaders,
        IReadOnlyList<ModrinthGameVersionTag> GameVersions);

    public static class ModrinthVersionExtensions
    {
        public static ModrinthVersionFile? PrimaryFile(this ModrinthVersion version)
        {
            var candidates = version.Files.Where(f => f.FileType is null).ToList();
            if (candidates.Count == 0)
                return null;

            return candidates.FirstOrDefault(f => f.Primary) ?? candidates[0];
        }

        public static bool IsRelease(this ModrinthVersion version) =>
            string.Equals(version.VersionType, "release", StringComparison.OrdinalIgnoreCase);
    }
}
