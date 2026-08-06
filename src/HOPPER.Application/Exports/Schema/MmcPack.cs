using System.Text.Json.Serialization;

namespace HOPPER.Application.Exports.Schema
{
    public sealed record MmcPack
    {
        [JsonPropertyName("formatVersion")]
        public int FormatVersion { get; init; } = 1;

        [JsonPropertyName("components")]
        public required IReadOnlyList<MmcComponent> Components { get; init; }
    }

    public sealed record MmcComponent
    {
        [JsonPropertyName("uid")]
        public required string Uid { get; init; }

        [JsonPropertyName("version")]
        public required string Version { get; init; }

        [JsonPropertyName("important")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Important { get; init; }
    }
}
