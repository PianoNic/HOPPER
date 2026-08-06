using System.Text.Json.Serialization;

namespace HOPPER.Application.Dtos.Manifest
{
    /// <summary>The whole body of GET /api/manifest. It is returned bare - never wrapped in an
    /// envelope - because the Java client parses the root object's "mods" array directly.</summary>
    public record ManifestDto
    {
        [JsonPropertyName("mods")] public required IReadOnlyList<ManifestModDto> Mods { get; init; }
    }
}
