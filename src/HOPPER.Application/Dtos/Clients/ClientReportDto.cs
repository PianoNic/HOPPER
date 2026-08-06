using System.Text.Json.Serialization;

namespace HOPPER.Application.Dtos.Clients
{
    public record ClientReportDto
    {
        [JsonPropertyName("clientId")] public required string ClientId { get; init; }

        [JsonPropertyName("username")] public required string? Username { get; init; }

        [JsonPropertyName("mods")] public required IReadOnlyList<ClientReportModDto> Mods { get; init; }
    }
}
