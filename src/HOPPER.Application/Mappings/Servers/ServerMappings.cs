using HOPPER.Application.Dtos.Servers;
using HOPPER.Domain;

namespace HOPPER.Application.Mappings.Servers
{
    public static class ServerMappings
    {
        public static ServerDto ToDto(this Server s, int modCount, int clientCount) => new()
        {
            Id = s.Id,
            Name = s.Name,
            Slug = s.Slug,
            ModCount = modCount,
            ClientCount = clientCount,
            CreatedAt = s.CreatedAt,
            MinecraftVersion = s.MinecraftVersion,
            Loader = s.Loader,
            LoaderVersion = s.LoaderVersion,
            IconSha256 = s.IconSha256,
            BytesServed = s.BytesServed,
        };

        public static ServerTokenDto ToTokenDto(this Server s) => new()
        {
            ServerId = s.Id,
            Token = s.Token,
        };
    }
}
