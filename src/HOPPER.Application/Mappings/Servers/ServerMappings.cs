using HOPPER.Application.Dtos.Servers;
using HOPPER.Domain;

namespace HOPPER.Application.Mappings.Servers
{
    public static class ServerMappings
    {
        /// <summary>The counts are passed in rather than read off the entity because the model has no
        /// navigation properties - the handler aggregates and hands the result here.</summary>
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
        };

        public static ServerTokenDto ToTokenDto(this Server s) => new()
        {
            ServerId = s.Id,
            Token = s.Token,
        };
    }
}
