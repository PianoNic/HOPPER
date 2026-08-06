using HOPPER.Domain.Enums;

namespace HOPPER.Domain
{
    public class Server : BaseEntity
    {
        public required string Name { get; set; }

        public required string Slug { get; set; }

        public required string Token { get; set; }

        public string? MinecraftVersion { get; set; }

        public ModLoader Loader { get; set; } = ModLoader.Unknown;

        public string? LoaderVersion { get; set; }
    }
}
