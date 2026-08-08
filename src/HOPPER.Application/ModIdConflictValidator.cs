using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application
{
    public sealed class DuplicateModIdException(string modId, string otherFileName, SyncSide side)
        : InvalidOperationException(
            $"{otherFileName} already declares the mod id '{modId}', and both would reach "
            + (side == SyncSide.Server ? "the dedicated server" : "a player")
            + ". A loader refuses to start with two copies of one mod, so set one of them to "
            + (side == SyncSide.Server ? "Client only" : "Server only")
            + ", or remove the other jar.")
    {
        public string ModId { get; } = modId;

        public string OtherFileName { get; } = otherFileName;

        public SyncSide Side { get; } = side;
    }

    public static class ModIdConflictValidator
    {
        /// A mod id may appear twice on a server only as Client only plus Server only, so that no
        /// machine is ever sent both. Every other pairing is a launch failure waiting to happen.
        public static async Task RefuseIfClaimedAsync(
            HopperDbContext db,
            Guid serverId,
            IReadOnlyList<string>? modIds,
            ModSide side,
            Guid? ignoring = null,
            CancellationToken cancellationToken = default)
        {
            if (modIds is null || modIds.Count == 0)
                return;

            var ids = modIds.Distinct(StringComparer.Ordinal).ToList();

            // Four columns for one server, not the rows themselves: a nested Contains inside Any is
            // not translatable, and a server's mod-id list is small enough to intersect here.
            var others = await db.Mods.AsNoTracking()
                .Where(m => m.ServerId == serverId && m.ModIds != null)
                .Select(m => new { m.Id, m.FileName, m.Side, m.ModIds })
                .ToListAsync(cancellationToken);

            foreach (var other in others)
            {
                if (other.Id == ignoring)
                    continue;

                if (ModSideRules.SharedSide(side, other.Side) is not { } shared)
                    continue;

                var clash = other.ModIds!.FirstOrDefault(id => ids.Contains(id, StringComparer.Ordinal));
                if (clash is null)
                    continue;

                throw new DuplicateModIdException(clash, other.FileName, shared);
            }
        }

        /// The guard only refuses new arrivals. A server that predates it can already hold a pair,
        /// and those are exactly the servers whose clients fail to launch, so they have to be visible.
        public static Dictionary<Guid, SyncSide> Collisions(IReadOnlyList<Mod> mods)
        {
            var byModId = new Dictionary<string, List<Mod>>(StringComparer.Ordinal);

            foreach (var mod in mods)
            {
                foreach (var id in mod.ModIds ?? [])
                {
                    if (!byModId.TryGetValue(id, out var claiming))
                        byModId[id] = claiming = [];

                    claiming.Add(mod);
                }
            }

            var found = new Dictionary<Guid, SyncSide>();

            foreach (var claiming in byModId.Values.Where(c => c.Count > 1))
            {
                for (var i = 0; i < claiming.Count; i++)
                {
                    for (var j = i + 1; j < claiming.Count; j++)
                    {
                        if (Conflict(claiming[i], claiming[j]) is not { } side)
                            continue;

                        found[claiming[i].Id] = side;
                        found[claiming[j].Id] = side;
                    }
                }
            }

            return found;
        }

        public static SyncSide? Conflict(Mod a, Mod b)
        {
            if (a.ModIds is null || b.ModIds is null)
                return null;

            return a.ModIds.Intersect(b.ModIds, StringComparer.Ordinal).Any()
                ? ModSideRules.SharedSide(a.Side, b.Side)
                : null;
        }
    }
}
