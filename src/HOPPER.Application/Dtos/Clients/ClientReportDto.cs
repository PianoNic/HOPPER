using System.Text.Json.Serialization;

namespace HOPPER.Application.Dtos.Clients
{
    /// <summary>Body of POST /api/clients/report, sent by the Forge locator after a successful sync.
    /// Names are pinned with [JsonPropertyName] so a later change to the global naming policy cannot
    /// silently start rejecting reports from clients already in the wild.</summary>
    public record ClientReportDto
    {
        [JsonPropertyName("clientId")] public required string ClientId { get; init; }

        /// <summary>Nullable on purpose, and the nullability is part of the fixed contract. A
        /// dedicated server — or any launcher invoked without --username — has no username to send,
        /// and the Java client sets Gson's serializeNulls() precisely so it goes out as an explicit
        /// "username": null. A non-nullable property here makes model binding answer that body with a
        /// 400 before any handler runs, and because Syncer.report() swallows every failure the client
        /// simply never appears on the dashboard. Still `required`, so the property must be present:
        /// a report that omits the field altogether is a different client than the one we shipped.</summary>
        [JsonPropertyName("username")] public required string? Username { get; init; }

        [JsonPropertyName("mods")] public required IReadOnlyList<ClientReportModDto> Mods { get; init; }
    }
}
