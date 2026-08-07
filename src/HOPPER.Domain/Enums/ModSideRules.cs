using System.Linq.Expressions;

namespace HOPPER.Domain.Enums
{
    public static class ModSideRules
    {
        public static ModSide WithheldFrom(SyncSide caller) =>
            caller == SyncSide.Server ? ModSide.ClientOnly : ModSide.ServerOnly;

        public static bool Reaches(ModSide mod, SyncSide caller) => mod != WithheldFrom(caller);

        public static Expression<Func<Mod, bool>> ReachesExpression(SyncSide caller)
        {
            var withheld = WithheldFrom(caller);
            return m => m.Side != withheld;
        }

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
