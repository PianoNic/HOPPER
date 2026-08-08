using HOPPER.Application.Loaders;
using HOPPER.Domain.Enums;

namespace HOPPER.Application.Exports
{
    public static class LoaderIds
    {
        private const string NotSet = "Set this server's loader before exporting a pack.";

        public static string MrpackKey(ModLoader loader) => LoaderDescriptors.Require(loader, NotSet).MrpackKey;

        public static string CurseForgePrefix(ModLoader loader) => LoaderDescriptors.Require(loader, NotSet).CurseForgePrefix;

        public static string PrismUid(ModLoader loader) => LoaderDescriptors.Require(loader, NotSet).PrismUid;

        public const string MinecraftUid = "net.minecraft";

        public static ModLoader FromPrismUid(string? uid) => LoaderDescriptors.ByPrismUid(uid);

        public static ModLoader FromMrpackKey(string? key) => LoaderDescriptors.ByMrpackKey(key);

        public static ModLoader FromCurseForgeId(string? id)
        {
            if (string.IsNullOrWhiteSpace(id))
                return ModLoader.Unknown;

            var dash = id.IndexOf('-');

            return LoaderDescriptors.ByCurseForgePrefix(dash < 0 ? id : id[..dash]);
        }
    }
}
