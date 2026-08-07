using HOPPER.Domain.Enums;

namespace HOPPER.Application.Imports
{
    public sealed record PackPlatform(string? MinecraftVersion, ModLoader Loader)
    {
        public static readonly PackPlatform Unknown = new(null, ModLoader.Unknown);
    }

    public sealed record PackPlanContext
    {
        public static readonly PackPlanContext Default = new();

        public PackPlatform Target { get; init; } = PackPlatform.Unknown;

        public long MaxMetadataBytes { get; init; } = HopperLimits.DefaultMaxPackMetadataBytes;
    }

    public static class PackPlatformCheck
    {
        public static IReadOnlyList<string> Verify(PackPlatform declared, PackPlatform target)
        {
            if (declared.Loader != ModLoader.Unknown
                && target.Loader != ModLoader.Unknown
                && declared.Loader != target.Loader)
            {
                throw new PackImportException(
                    $"This pack is a {declared.Loader} pack and this server is set to {target.Loader}. Every jar in it would be ignored.");
            }

            if (!string.IsNullOrWhiteSpace(declared.MinecraftVersion)
                && !string.IsNullOrWhiteSpace(target.MinecraftVersion)
                && !string.Equals(declared.MinecraftVersion, target.MinecraftVersion, StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    $"This pack is built for Minecraft {declared.MinecraftVersion} and this server is set to {target.MinecraftVersion}. Some of its mods may not load.",
                ];
            }

            return [];
        }
    }
}
