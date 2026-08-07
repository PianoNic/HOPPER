using HOPPER.Domain.Enums;

namespace HOPPER.Infrastructure.Services
{
    public static class LocatorTemplates
    {
        public sealed record Template(string FileName, string MarkerEntry);

        private const string ForgeService = "META-INF/services/net.minecraftforge.forgespi.locating.IModLocator";
        private const string NeoForgeService = "META-INF/services/net.neoforged.neoforgespi.locating.IModFileCandidateLocator";

        private static readonly Template Forge1122 = new("hopper-forge-1122.jar", "ch/pianonic/hopper/HopperCoreMod.class");

        private static readonly Template Forge1165 = new("hopper-forge-1165.jar", ForgeService);
        private static readonly Template Forge1182 = new("hopper-forge-1182.jar", ForgeService);
        private static readonly Template ForgeModern = new("hopper-forge-modern.jar", ForgeService);
        private static readonly Template NeoForge = new("hopper-neoforge.jar", NeoForgeService);
        private static readonly Template Fabric = new("hopper-fabric.jar", "fabric.mod.json");

        private static readonly Template QuiltPlugin = new("hopper-quilt-plugin.jar", "quilt.mod.json");

        public const string QuiltPluginVariant = "quilt-plugin";

        public static Template For(ModLoader loader, string? minecraftVersion, string? variant = null)
        {
            if (!string.IsNullOrWhiteSpace(variant))
            {
                return loader == ModLoader.Quilt && string.Equals(variant, QuiltPluginVariant, StringComparison.Ordinal)
                    ? QuiltPlugin
                    : throw new LocatorVariantNotAvailableException(variant, loader);
            }

            return loader switch
            {
                ModLoader.Forge => ForForge(minecraftVersion),
                ModLoader.NeoForge => NeoForge,
                ModLoader.Fabric => Fabric,

                ModLoader.Quilt => Fabric,

                _ => throw new LocatorLoaderNotConfiguredException(),
            };
        }

        private static Template ForForge(string? minecraftVersion) => MinorOf(minecraftVersion) switch
        {
            <= 12 => Forge1122,
            <= 16 => Forge1165,
            <= 18 => Forge1182,
            _ => ForgeModern,
        };

        private static int MinorOf(string? minecraftVersion)
        {
            var value = minecraftVersion?.Trim();
            if (string.IsNullOrEmpty(value) || !value.StartsWith("1.", StringComparison.Ordinal))
                return int.MaxValue;

            var rest = value.AsSpan(2);
            var digits = 0;
            while (digits < rest.Length && char.IsAsciiDigit(rest[digits]))
                digits++;

            return digits > 0 && int.TryParse(rest[..digits], out var minor) ? minor : int.MaxValue;
        }
    }

    public sealed class LocatorLoaderNotConfiguredException()
        : InvalidOperationException("Set this server's loader before downloading its client jar.");

    public sealed class LocatorVariantNotAvailableException(string variant, ModLoader loader)
        : InvalidOperationException($"There is no '{variant}' client jar for a {loader} server. "
            + "The Quilt plugin jar is Quilt-only and needs -Dloader.experimental.allow_loading_plugins=true; "
            + "without that flag use the default download.");
}
