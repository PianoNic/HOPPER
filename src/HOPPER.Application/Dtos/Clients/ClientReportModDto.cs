using System.Text.Json.Serialization;

namespace HOPPER.Application.Dtos.Clients
{
    /// <summary>One jar as the client found it on its own disk. Inbound half of the fixed wire
    /// format, so the names are pinned for the same reason the manifest's are.</summary>
    public record ClientReportModDto
    {
        [JsonPropertyName("file")] public required string File { get; init; }

        [JsonPropertyName("sha256")] public required string Sha256 { get; init; }
    }
}
