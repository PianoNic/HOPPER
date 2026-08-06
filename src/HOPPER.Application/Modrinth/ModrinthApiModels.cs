using System.Text.Json;
using System.Text.Json.Serialization;

namespace HOPPER.Application.Modrinth
{
    /// <summary>How a Modrinth response is read. Lives next to the models rather than inside the
    /// client so the parsing rules can be asserted directly - the point of them is what happens on a
    /// response nobody anticipated, which is exactly what a live call will not show you.
    ///
    /// Resilient by construction: unknown members are ignored (System.Text.Json's default), a number
    /// that arrives as a string still parses, and every model supplies a default for what is absent.
    /// Modrinth add fields without notice and mark most of them optional; a browser that throws on an
    /// unexpected shape breaks on their schedule rather than ours.</summary>
    public static class ModrinthJson
    {
        public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
        {
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
        };
    }

    /// <summary>The raw shapes of the Modrinth v2 API, named after the API rather than after HOPPER.
    ///
    /// Every member carries an explicit [JsonPropertyName] because the API is snake_case and these
    /// names are an external contract, not ours to tidy. Every member also has a default, and every
    /// collection normalises null to empty on the way in: Modrinth add fields without warning, mark
    /// most of them optional, and a browser that throws on an unexpected shape would break the moment
    /// they ship one. Unknown members are ignored by System.Text.Json by default, which is the other
    /// half of the same rule.
    ///
    /// The single easiest bug in this feature is confusing a search hit with a project. A hit says
    /// project_id and its "versions" are GAME versions; a project says id, its "game_versions" are
    /// game versions and its "versions" are VERSION IDS. They are modelled separately for that reason
    /// and are never interchanged.</summary>
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

        /// <summary>Loaders are folded in here by the search endpoint - "forge" is a category on a
        /// hit, not a separate field. Only /project/{id} splits them out.</summary>
        [JsonPropertyName("categories")]
        public IReadOnlyList<string> Categories { get => _categories; init => _categories = value ?? []; }

        /// <summary>Minecraft versions, despite the name. A project's "versions" are version ids.</summary>
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

        /// <summary>On a project these are a real, separate field. On a search hit they are not.</summary>
        [JsonPropertyName("loaders")]
        public IReadOnlyList<string> Loaders { get => _loaders; init => _loaders = value ?? []; }

        [JsonPropertyName("game_versions")]
        public IReadOnlyList<string> GameVersions { get => _gameVersions; init => _gameVersions = value ?? []; }

        /// <summary>Version IDS, not Minecraft versions.</summary>
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

        /// <summary>release | beta | alpha. Kept as a string rather than an enum: an unknown value
        /// must not fail deserialisation, and the only decision made on it is "is this a release".</summary>
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

        /// <summary>Only ever populated by the single-version endpoint. The list endpoint is always
        /// asked with include_changelog=false, which is a 35% smaller response on a narrow query.</summary>
        [JsonPropertyName("changelog")]
        public string? Changelog { get; init; }
    }

    public sealed record ModrinthVersionFile
    {
        private readonly IReadOnlyDictionary<string, string> _hashes = new Dictionary<string, string>();

        /// <summary>Modrinth publish exactly sha1 and sha512, and NEVER sha256. That single fact is
        /// why a mod added from the browser has to be downloaded and hashed server-side: sha256 is the
        /// blob address and the pinned wire format's hash, and no upstream will ever hand it over.</summary>
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

        /// <summary>Non-null marks an extra - a resource pack shipped alongside a datapack, say - and
        /// never the mod jar itself.</summary>
        [JsonPropertyName("file_type")]
        public string? FileType { get; init; }

        public string? Sha1 => Hashes.TryGetValue("sha1", out var value) ? value : null;

        public string? Sha512 => Hashes.TryGetValue("sha512", out var value) ? value : null;
    }

    /// <summary>The only REQUIRED member is dependency_type. version_id, project_id and file_name are
    /// all nullable in the published schema and all three shapes turn up live, so every consumer has
    /// to handle "pinned", "any version of this project" and "not identifiable at all".</summary>
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

    /// <summary>One entry of GET /tag/game_version.</summary>
    public sealed record ModrinthGameVersionTag
    {
        [JsonPropertyName("version")]
        public string Version { get; init; } = string.Empty;

        [JsonPropertyName("version_type")]
        public string? VersionType { get; init; }

        [JsonPropertyName("major")]
        public bool Major { get; init; }
    }

    /// <summary>One entry of GET /tag/loader. The API also returns an "icon" holding a full inline
    /// SVG per entry; it is deliberately not modelled, so it is dropped at the parse boundary and
    /// never reaches the dashboard.</summary>
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

    /// <summary>Both tag lists in one object, because the browser needs both to populate its filters
    /// and they are effectively static.</summary>
    public sealed record ModrinthTags(
        IReadOnlyList<ModrinthLoaderTag> Loaders,
        IReadOnlyList<ModrinthGameVersionTag> GameVersions);

    public static class ModrinthVersionExtensions
    {
        /// <summary>The jar. The published rule is that at most one file carries primary=true and that
        /// when none does, the first file is the primary one. Files with a non-null file_type are
        /// extras and are never the mod jar, so they are excluded before that rule is applied.</summary>
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
