using System.Text.Json.Serialization;

namespace HOPPER.Application.Exports.Schema
{
    public sealed record CurseForgeManifest
    {
        [JsonPropertyName("minecraft")]
        public required CurseForgeMinecraft Minecraft { get; init; }

        [JsonPropertyName("manifestType")]
        public string ManifestType { get; init; } = "minecraftModpack";

        [JsonPropertyName("manifestVersion")]
        public int ManifestVersion { get; init; } = 1;

        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("version")]
        public required string Version { get; init; }

        [JsonPropertyName("author")]
        public required string Author { get; init; }

        [JsonPropertyName("overrides")]
        public string Overrides { get; init; } = "overrides";

        [JsonPropertyName("files")]
        public required IReadOnlyList<CurseForgeFileEntry> Files { get; init; }
    }

    public sealed record CurseForgeMinecraft
    {
        [JsonPropertyName("version")]
        public required string Version { get; init; }

        [JsonPropertyName("modLoaders")]
        public required IReadOnlyList<CurseForgeModLoader> ModLoaders { get; init; }
    }

    public sealed record CurseForgeModLoader
    {
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("primary")]
        public bool Primary { get; init; } = true;
    }

    public sealed record CurseForgeFileEntry
    {
        [JsonPropertyName("projectID")]
        public required int ProjectId { get; init; }

        [JsonPropertyName("fileID")]
        public required int FileId { get; init; }

        [JsonPropertyName("required")]
        public bool Required { get; init; } = true;
    }
}
