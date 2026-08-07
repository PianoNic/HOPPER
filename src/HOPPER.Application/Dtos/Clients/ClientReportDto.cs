using System.Text.Json.Serialization;

namespace HOPPER.Application.Dtos.Clients
{
    public record ClientReportDto
    {
        [JsonPropertyName("clientId")] public required string ClientId { get; init; }

        [JsonPropertyName("username")] public required string? Username { get; init; }

        /// <summary>
        /// Optional, and absent means client. Every jar shipped before sides existed sends no side
        /// and has to keep being understood, which is the same rule the manifest follows.
        /// </summary>
        [JsonPropertyName("side")] public string? Side { get; init; }

        [JsonPropertyName("mods")] public required IReadOnlyList<ClientReportModDto> Mods { get; init; }
    }
}
