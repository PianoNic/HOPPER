using HOPPER.Application.Dtos.Clients;
using HOPPER.Domain;

namespace HOPPER.Application.Mappings.Clients
{
    public static class ClientMappings
    {
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
