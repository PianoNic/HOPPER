using System.Text.Json.Serialization;

namespace HOPPER.Application.Exports.Schema
{
    public sealed record MrpackIndex
    {
        [JsonPropertyName("formatVersion")]
        public int FormatVersion { get; init; } = 1;

        [JsonPropertyName("game")]
        public string Game { get; init; } = "minecraft";

        [JsonPropertyName("versionId")]
        public required string VersionId { get; init; }

        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("summary")]
        public string? Summary { get; init; }

        [JsonPropertyName("files")]
        public required IReadOnlyList<MrpackFile> Files { get; init; }

        [JsonPropertyName("dependencies")]
        public required IReadOnlyDictionary<string, string> Dependencies { get; init; }
    }

    public sealed record MrpackFile
    {
        [JsonPropertyName("path")]
        public required string Path { get; init; }

        [JsonPropertyName("hashes")]
        public required IReadOnlyDictionary<string, string> Hashes { get; init; }

        [JsonPropertyName("env")]
        public MrpackEnv? Env { get; init; }

        [JsonPropertyName("downloads")]
        public required IReadOnlyList<string> Downloads { get; init; }

        [JsonPropertyName("fileSize")]
        public required long FileSize { get; init; }
    }

    public sealed record MrpackEnv
    {
        [JsonPropertyName("client")]
        public string Client { get; init; } = "required";

        [JsonPropertyName("server")]
        public string Server { get; init; } = "required";
    }
}
