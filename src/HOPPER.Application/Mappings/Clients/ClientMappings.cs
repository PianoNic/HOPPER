using HOPPER.Application.Dtos.Clients;
using HOPPER.Domain;

namespace HOPPER.Application.Mappings.Clients
{
    public static class ClientMappings
    {
        /// <summary>The reported jars are passed in rather than read off the entity because the model
        /// has no navigation properties — the handler does the grouping and hands the result here.</summary>
        public static ClientDto ToDto(this Client c, IReadOnlyList<ClientModDto> mods) => new()
        {
            Id = c.Id,
            ClientId = c.ClientId,
            Username = c.Username,
            LastSeenAt = c.LastSeenAt,
            LastIpAddress = c.LastIpAddress,
            Mods = mods,
            CreatedAt = c.CreatedAt,
        };
    }
}
