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
    }
}
