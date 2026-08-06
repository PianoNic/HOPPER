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

        public static Template For(ModLoader loader, string? minecraftVersion) => loader switch
        {
            ModLoader.Forge => ForForge(minecraftVersion),
            ModLoader.NeoForge => NeoForge,
            ModLoader.Fabric => Fabric,

            ModLoader.Quilt => Fabric,

            _ => throw new LocatorLoaderNotConfiguredException(),
        };

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
}
