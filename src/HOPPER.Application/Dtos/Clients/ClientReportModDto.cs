using System.Text.Json.Serialization;

namespace HOPPER.Application.Dtos.Clients
{
    public record ClientReportModDto
    {
        [JsonPropertyName("file")] public required string File { get; init; }

        [JsonPropertyName("sha256")] public required string Sha256 { get; init; }
    }
}
