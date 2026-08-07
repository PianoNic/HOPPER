using System.Linq.Expressions;

namespace HOPPER.Domain.Enums
{
    public static class ModSideRules
    {
        public static bool Reaches(ModSide mod, SyncSide caller) => mod switch
        {
            ModSide.ClientOnly => caller == SyncSide.Client,
            ModSide.ServerOnly => caller == SyncSide.Server,
            _ => true,
        };

        public static Expression<Func<Mod, bool>> ReachesExpression(SyncSide caller) => caller switch
        {
            SyncSide.Server => m => m.Side == ModSide.Both || m.Side == ModSide.ServerOnly,
            _ => m => m.Side == ModSide.Both || m.Side == ModSide.ClientOnly,
        };

        public static bool TryParse(string? value, out SyncSide side)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                side = SyncSide.Client;
                return true;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "client":
                    side = SyncSide.Client;
                    return true;
                case "server":
                    side = SyncSide.Server;
                    return true;
                default:
                    side = SyncSide.Client;
                    return false;
            }
        }
    }
}
