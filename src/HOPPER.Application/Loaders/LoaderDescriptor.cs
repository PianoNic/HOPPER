using HOPPER.Domain.Enums;

namespace HOPPER.Application.Loaders
{
    public sealed record LoaderDescriptor(
        ModLoader Loader,
        string MrpackKey,
        string CurseForgePrefix,
        string PrismUid,
        string ModrinthFacet,
        IReadOnlyList<string> AlsoRuns);

    public static class LoaderDescriptors
    {
        // The one table. Every other spelling of a loader in C# reads from here.
        private static readonly LoaderDescriptor[] All =
        [
            new(ModLoader.Forge, "forge", "forge", "net.minecraftforge", "forge", []),
            new(ModLoader.NeoForge, "neoforge", "neoforge", "net.neoforged", "neoforge", []),
            new(ModLoader.Fabric, "fabric-loader", "fabric", "net.fabricmc.fabric-loader", "fabric", []),

            // Quilt runs Fabric mods through quilted_fabric_loader, and the mods a Quilt server
            // needs are largely published under the fabric facet only - Fabric API among them.
            new(ModLoader.Quilt, "quilt-loader", "quilt", "org.quiltmc.quilt-loader", "quilt", ["fabric"]),
        ];

        public static IReadOnlyList<LoaderDescriptor> Known => All;

        public static LoaderDescriptor? For(ModLoader loader) =>
            All.FirstOrDefault(d => d.Loader == loader);

        public static LoaderDescriptor Require(ModLoader loader, string whenMissing) =>
            For(loader) ?? throw new ServerPlatformNotConfiguredException(whenMissing);

        /// The facet itself plus anything that loader also runs, so a Quilt server is offered the
        /// Fabric builds it can actually load.
        public static IReadOnlyList<string> RunnableBy(string facet)
        {
            var descriptor = All.FirstOrDefault(d => string.Equals(d.ModrinthFacet, facet, StringComparison.Ordinal));
            if (descriptor is null || descriptor.AlsoRuns.Count == 0)
                return [facet];

            return [facet, .. descriptor.AlsoRuns];
        }

        public static ModLoader ByPrismUid(string? uid) =>
            All.FirstOrDefault(d => string.Equals(d.PrismUid, uid, StringComparison.Ordinal))?.Loader
            ?? ModLoader.Unknown;

        public static ModLoader ByMrpackKey(string? key) =>
            All.FirstOrDefault(d => string.Equals(d.MrpackKey, key, StringComparison.OrdinalIgnoreCase))?.Loader
            ?? ModLoader.Unknown;

        public static ModLoader ByCurseForgePrefix(string? prefix) =>
            All.FirstOrDefault(d => string.Equals(d.CurseForgePrefix, prefix, StringComparison.OrdinalIgnoreCase))?.Loader
            ?? ModLoader.Unknown;
    }
}
