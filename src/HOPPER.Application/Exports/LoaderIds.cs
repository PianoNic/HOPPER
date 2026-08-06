using HOPPER.Domain.Enums;

namespace HOPPER.Application.Exports
{
    /// <summary>One loader, four external names. All three exporters read this table rather than each
    /// spelling the mapping out, because they are the same fact expressed four ways and a drift
    /// between them is a pack that imports into one launcher and not another.
    ///
    /// The loader VERSION is never prefixed with the Minecraft version by any of these. A CurseForge
    /// manifest id is "forge-47.4.10" and not "forge-1.20.1-47.4.10"; the .mrpack dependency and the
    /// Prism component both carry the bare build number.</summary>
    public static class LoaderIds
    {
        /// <summary>Key under .mrpack dependencies. The format allows exactly minecraft, forge,
        /// neoforge, fabric-loader and quilt-loader - an unrecognised key is a hard "Unknown
        /// dependency type" in Prism, so export emits only from this set even though import has to
        /// tolerate whatever Modrinth add later.</summary>
        public static string MrpackKey(ModLoader loader) => loader switch
        {
            ModLoader.Forge => "forge",
            ModLoader.NeoForge => "neoforge",
            ModLoader.Fabric => "fabric-loader",
            ModLoader.Quilt => "quilt-loader",
            _ => throw new ServerPlatformNotConfiguredException("Set this server's loader before exporting a pack."),
        };

        /// <summary>Prefix of the CurseForge manifest's minecraft.modLoaders[].id, which is
        /// "&lt;loader&gt;-&lt;version&gt;".</summary>
        public static string CurseForgePrefix(ModLoader loader) => loader switch
        {
            ModLoader.Forge => "forge",
            ModLoader.NeoForge => "neoforge",
            ModLoader.Fabric => "fabric",
            ModLoader.Quilt => "quilt",
            _ => throw new ServerPlatformNotConfiguredException("Set this server's loader before exporting a pack."),
        };

        /// <summary>Component uid in mmc-pack.json, as published by meta.prismlauncher.org.</summary>
        public static string PrismUid(ModLoader loader) => loader switch
        {
            ModLoader.Forge => "net.minecraftforge",
            ModLoader.NeoForge => "net.neoforged",
            ModLoader.Fabric => "net.fabricmc.fabric-loader",
            ModLoader.Quilt => "org.quiltmc.quilt-loader",
            _ => throw new ServerPlatformNotConfiguredException("Set this server's loader before exporting a pack."),
        };

        /// <summary>Prism's uid for Minecraft itself, the one component every instance carries.</summary>
        public const string MinecraftUid = "net.minecraft";
    }
}
