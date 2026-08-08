using HOPPER.Application.Loaders;
using HOPPER.Domain;
using HOPPER.Domain.Enums;

namespace HOPPER.Application
{
    public static class ServerPlatform
    {
        public static (string MinecraftVersion, string Loader) RequireForBrowsing(Server server)
        {
            if (string.IsNullOrWhiteSpace(server.MinecraftVersion) || server.Loader == ModLoader.Unknown)
                throw new ServerPlatformNotConfiguredException("Set this server's Minecraft version and loader before browsing Modrinth.");

            return (server.MinecraftVersion.Trim(), LoaderFacet(server.Loader));
        }

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

        public static string LoaderFacet(ModLoader loader) =>
            LoaderDescriptors.Require(loader, "Set this server's loader first.").ModrinthFacet;

        public static string? NormaliseVersion(string? value, string what)
        {
            var trimmed = value?.Trim();
            if (string.IsNullOrEmpty(trimmed))
                return null;

            if (trimmed.Length > 32)
                throw new InvalidVersionException($"{what} is too long.");

            foreach (var c in trimmed)
            {
                if (!char.IsAsciiLetterOrDigit(c) && c != '.' && c != '_' && c != '-' && c != '+')
                    throw new InvalidVersionException($"{what} may only contain letters, digits, dots, dashes, underscores and plus signs.");
            }

            return trimmed;
        }
    }

    public sealed class InvalidVersionException(string message) : RuleViolationException(message);

    public sealed class ServerPlatformNotConfiguredException(string message) : Exception(message);
}
