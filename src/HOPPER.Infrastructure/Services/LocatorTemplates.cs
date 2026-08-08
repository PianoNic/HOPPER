using HOPPER.Domain.Enums;
using Microsoft.Extensions.Configuration;

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

        public const string QuiltPluginVariant = "quilt-plugin";

        /* Against the app's own directory, never the process working directory. The two are the
           same in the container and differ under `dotnet run`, which is how the readiness probe
           came to answer 503 for a directory the jar endpoint was serving from happily. */
        public static string ResolveDirectory(IConfiguration configuration)
        {
            var configured = configuration["Hopper:LocatorTemplateDirectory"];

            return string.IsNullOrWhiteSpace(configured)
                ? Path.Combine(AppContext.BaseDirectory, "locator")
                : Path.GetFullPath(configured, AppContext.BaseDirectory);
        }

        public static Template For(ModLoader loader, string? minecraftVersion, string? variant = null)
        {
            // No variant resolves today. hopper-quilt-plugin.jar still builds - it is correct code
            // waiting on Quilt - but serving it stops a client booting rather than degrading.
            if (!string.IsNullOrWhiteSpace(variant))
                throw new LocatorVariantNotAvailableException(variant, loader);

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
        : InvalidOperationException("Set this server's loader before downloading its jar.");

    public sealed class LocatorVariantNotAvailableException(string variant, ModLoader loader)
        : InvalidOperationException($"There is no '{variant}' jar to download for a {loader} server. "
            + "Quilt Loader refuses to load a third-party plugin - with the experimental flag unset it "
            + "will not parse the jar, and with it set its own plugin classloader fails - so the plugin "
            + "jar stops a client starting instead of degrading. Use the default download, which Quilt "
            + "runs through quilted_fabric_loader.");
}
