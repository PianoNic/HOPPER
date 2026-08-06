using HOPPER.Domain.Enums;

namespace HOPPER.Infrastructure.Services
{
    /// <summary>Which template jar a server gets, and how to recognise it once opened.
    ///
    /// The locator is one shared core plus one thin adapter per loader generation, and a loader
    /// resolves ONE jar out of mods/ and nothing else - so every adapter jar is self-contained and
    /// exactly one of them is correct for any given server. This is the table that picks it.
    ///
    /// Forge alone needs four, because IModLocator keeps its name and changes its signature across
    /// generations; the ranges below are the ones settings.gradle documents against the forgespi
    /// each Minecraft version actually ships.</summary>
    public static class LocatorTemplates
    {
        /// <summary>File name of the template inside the locator directory, and the archive entry
        /// that proves the jar really is that adapter.
        ///
        /// The marker differs per loader because the declaration does: Forge and NeoForge register
        /// through META-INF/services, Fabric and Quilt through a json at the archive root. Checking
        /// the wrong one would reject a perfectly good jar.</summary>
        public sealed record Template(string FileName, string MarkerEntry);

        private const string ForgeService = "META-INF/services/net.minecraftforge.forgespi.locating.IModLocator";
        private const string NeoForgeService = "META-INF/services/net.neoforged.neoforgespi.locating.IModFileCandidateLocator";

        /// <summary>Forge 1.12.x. A LaunchWrapper coremod rather than a locator, so it declares
        /// itself with an FMLCorePlugin manifest attribute and ships no services file at all - the
        /// coremod class is the only entry that can stand in as a marker.</summary>
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

            // Quilt gets the FABRIC jar, and that is the shipped default rather than a shortcut.
            // Quilt runs Fabric mods through StandardFabricPlugin, so the preLaunch entrypoint works
            // unchanged; the real QuiltLoaderPlugin (hopper-quilt-plugin.jar) hard-fails with a
            // ParseException unless the player passes -Dloader.experimental.allow_loading_plugins=true,
            // which is exactly the launcher configuration HOPPER exists to avoid. See
            // src/HOPPER.Locator/hopper-quilt/build.gradle, which spells out the same trade.
            ModLoader.Quilt => Fabric,

            _ => throw new LocatorLoaderNotConfiguredException(),
        };

        /// <summary>Forge's four generations. Read as "the newest adapter whose range this version
        /// has not passed", so a Minecraft release newer than anything listed lands on modern rather
        /// than failing - which is the right guess: the modern signature has held since 1.19.</summary>
        private static Template ForForge(string? minecraftVersion) => MinorOf(minecraftVersion) switch
        {
            <= 12 => Forge1122,
            <= 16 => Forge1165,
            <= 18 => Forge1182,
            _ => ForgeModern,
        };

        /// <summary>The minor of "1.20.1" is 20. Minecraft versions are not semver - "23w13a_or_b"
        /// and "1.21.1-rc1" are both real - so anything that does not start "1.&lt;digits&gt;" is
        /// reported as newest. A snapshot IS newer than every release, and a release candidate keeps
        /// the minor it is a candidate for.</summary>
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

    /// <summary>Mapped to 400. The server has no loader set, so there is no jar to hand out - and
    /// unlike a missing template this is the admin's own row to fix, not the deployment's.</summary>
    public sealed class LocatorLoaderNotConfiguredException()
        : InvalidOperationException("Set this server's loader before downloading its client jar.");
}
