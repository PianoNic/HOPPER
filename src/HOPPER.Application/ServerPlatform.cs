using HOPPER.Domain;
using HOPPER.Domain.Enums;

namespace HOPPER.Application
{
    /// <summary>The one place that decides whether a server has told HOPPER enough about itself.
    ///
    /// Two different amounts are enough for two different jobs, which is why there are two methods.
    /// Browsing and resolving dependencies need the Minecraft version and the loader, because those
    /// are the filters. Exporting a pack needs the loader VERSION as well, because a .mrpack
    /// dependency, a CurseForge modLoaders[].id and a Prism component all name an exact build.</summary>
    public static class ServerPlatform
    {
        /// <summary>Minecraft version and loader, for the browser and the dependency resolver.</summary>
        public static (string MinecraftVersion, string Loader) RequireForBrowsing(Server server)
        {
            if (string.IsNullOrWhiteSpace(server.MinecraftVersion) || server.Loader == ModLoader.Unknown)
                throw new ServerPlatformNotConfiguredException("Set this server's Minecraft version and loader before browsing Modrinth.");

            return (server.MinecraftVersion.Trim(), LoaderFacet(server.Loader));
        }

        /// <summary>Minecraft version, loader and loader version, for the exporters.</summary>
        public static (string MinecraftVersion, ModLoader Loader, string LoaderVersion) RequireForExport(Server server)
        {
            if (string.IsNullOrWhiteSpace(server.MinecraftVersion)
                || server.Loader == ModLoader.Unknown
                || string.IsNullOrWhiteSpace(server.LoaderVersion))
            {
                throw new ServerPlatformNotConfiguredException(
                    "Set this server's Minecraft version, loader and loader version before exporting a pack.");
            }

            return (server.MinecraftVersion.Trim(), server.Loader, server.LoaderVersion.Trim());
        }

        /// <summary>The lowercase name Modrinth uses for a loader, in both the search facet and the
        /// version endpoint's loaders parameter.</summary>
        public static string LoaderFacet(ModLoader loader) => loader switch
        {
            ModLoader.Forge => "forge",
            ModLoader.NeoForge => "neoforge",
            ModLoader.Fabric => "fabric",
            ModLoader.Quilt => "quilt",
            _ => throw new ServerPlatformNotConfiguredException("Set this server's loader first."),
        };

        /// <summary>Validates a version string typed by an admin. Minecraft and loader versions are not
        /// semver - "1.20.1", "23w13a_or_b" and "47.4.10" are all real - so the character set and the
        /// length are the only things worth asserting.</summary>
        public static string? NormaliseVersion(string? value, string what)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return null;

            if (trimmed.Length > 32)
                throw new ArgumentException($"{what} is too long.");

            foreach (var c in trimmed)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-' && c != '+')
                    throw new ArgumentException($"{what} may only contain letters, digits, dots, dashes, underscores and plus signs.");
            }

            return trimmed;
        }
    }

    /// <summary>Mapped to 400. Not a fault: it is a server the admin has not finished describing, and
    /// the message names exactly what to fill in.</summary>
    public sealed class ServerPlatformNotConfiguredException(string message) : Exception(message);
}
