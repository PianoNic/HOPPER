using System.Text.Json.Serialization;

namespace HOPPER.Application.Dtos.Manifest
{
    public record ManifestDto
    {
        [JsonPropertyName("mods")] public required IReadOnlyList<ManifestModDto> Mods { get; init; }
    }
}
