using HOPPER.Domain.Enums;

namespace HOPPER.Application.Exports
{
    public static class LoaderIds
    {
        public static string MrpackKey(ModLoader loader) => loader switch
        {
            ModLoader.Forge => "forge",
            ModLoader.NeoForge => "neoforge",
            ModLoader.Fabric => "fabric-loader",
            ModLoader.Quilt => "quilt-loader",
            _ => throw new ServerPlatformNotConfiguredException("Set this server's loader before exporting a pack."),
        };

        public static string CurseForgePrefix(ModLoader loader) => loader switch
        {
            ModLoader.Forge => "forge",
            ModLoader.NeoForge => "neoforge",
            ModLoader.Fabric => "fabric",
            ModLoader.Quilt => "quilt",
            _ => throw new ServerPlatformNotConfiguredException("Set this server's loader before exporting a pack."),
        };

        public static string PrismUid(ModLoader loader) => loader switch
        {
            ModLoader.Forge => "net.minecraftforge",
            ModLoader.NeoForge => "net.neoforged",
            ModLoader.Fabric => "net.fabricmc.fabric-loader",
            ModLoader.Quilt => "org.quiltmc.quilt-loader",
            _ => throw new ServerPlatformNotConfiguredException("Set this server's loader before exporting a pack."),
        };

        public const string MinecraftUid = "net.minecraft";

        public static ModLoader FromPrismUid(string? uid) => uid switch
        {
            "net.minecraftforge" => ModLoader.Forge,
            "net.neoforged" => ModLoader.NeoForge,
            "net.fabricmc.fabric-loader" => ModLoader.Fabric,
            "org.quiltmc.quilt-loader" => ModLoader.Quilt,
            _ => ModLoader.Unknown,
        };

        public static ModLoader FromMrpackKey(string? key) => key?.ToLowerInvariant() switch
        {
            "forge" => ModLoader.Forge,
            "neoforge" => ModLoader.NeoForge,
            "fabric-loader" => ModLoader.Fabric,
            "quilt-loader" => ModLoader.Quilt,
            _ => ModLoader.Unknown,
        };

        public static ModLoader FromCurseForgeId(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return ModLoader.Unknown;

            var dash = id.IndexOf('-');
            var prefix = (dash < 0 ? id : id[..dash]).ToLowerInvariant();

            return prefix switch
            {
                "forge" => ModLoader.Forge,
                "neoforge" => ModLoader.NeoForge,
                "fabric" => ModLoader.Fabric,
                "quilt" => ModLoader.Quilt,
                _ => ModLoader.Unknown,
            };
        }
    }
}
