using System.Text.Json.Serialization;

namespace HOPPER.Application.Dtos.Manifest
{
    public record ManifestModDto
    {
        [JsonPropertyName("file")] public required string File { get; init; }

        [JsonPropertyName("url")] public required string Url { get; init; }

        [JsonPropertyName("sha256")] public required string Sha256 { get; init; }

        [JsonPropertyName("size")] public required long Size { get; init; }

        [JsonPropertyName("modIds")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<string>? ModIds { get; init; }
    }
}
