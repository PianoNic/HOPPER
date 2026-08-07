using System.Linq.Expressions;

namespace HOPPER.Domain.Enums
{
    /// <summary>
    /// One rule, in one place, for whether a mod reaches a caller. The manifest and the blob
    /// endpoint both need it: filtering the list while leaving the bytes fetchable by hash would
    /// hand a dedicated server the client-only jar it was not sent.
    /// </summary>
    public static class ModSideRules
    {
        public static bool Reaches(ModSide mod, SyncSide caller) => mod switch
        {
            ModSide.ClientOnly => caller == SyncSide.Client,
            ModSide.ServerOnly => caller == SyncSide.Server,
            _ => true,
        };

        /// <summary>The same rule as an expression, so it runs in the database rather than after
        /// materialising every row.</summary>
        public static Expression<Func<Mod, bool>> ReachesExpression(SyncSide caller) => caller switch
        {
            SyncSide.Server => m => m.Side == ModSide.Both || m.Side == ModSide.ServerOnly,
            _ => m => m.Side == ModSide.Both || m.Side == ModSide.ClientOnly,
        };

        /// <summary>Parses the wire value. Absent is Client, because every jar already in the field
        /// sends nothing and must keep receiving exactly what it receives today.</summary>
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
