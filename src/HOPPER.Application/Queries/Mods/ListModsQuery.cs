using HOPPER.Domain;
using HOPPER.Application.ModMetadata;
using HOPPER.Infrastructure.Interfaces;
using HOPPER.Application.Dtos.Mods;
using HOPPER.Application.Mappings.Mods;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Mods
{
    public record ListModsQuery(Guid ServerId) : IQuery<IReadOnlyList<ModDto>>;

    public class ListModsQueryHandler(HopperDbContext db, IBlobStorage blobs)
        : IQueryHandler<ListModsQuery, IReadOnlyList<ModDto>>
    {
        public async ValueTask<IReadOnlyList<ModDto>> Handle(ListModsQuery query, CancellationToken cancellationToken)
        {
            var rows = await db.Mods.AsNoTracking()
                .Where(m => m.ServerId == query.ServerId)
                .OrderBy(m => m.FileName)
                .ToListAsync(cancellationToken);
            // Per distinct hash, not per row: blobs are shared.
            var present = rows.Select(m => m.Sha256).Distinct(StringComparer.Ordinal)
                .ToDictionary(sha => sha, blobs.Exists, StringComparer.Ordinal);

            var collisions = ModIdConflictValidator.Collisions(rows);

            // Bundled ids count for everyone: a loader extracts nested jars into the global mod
            // list, so a library one mod carries is loaded for the mod next to it as well.
            var provided = rows
                .SelectMany(m => (m.ModIds ?? []).Concat(m.BundledMods ?? []))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return rows
                .Select(m => m.ToDto() with
                {
                    BytesMissing = !present[m.Sha256],
                    // Not GetValueOrDefault: a missing key would come back as SyncSide.Client.
                    CollidesOn = collisions.TryGetValue(m.Id, out var side) ? side : null,
                    MissingDependencies = Missing(m, provided),
                })
                .ToList();
        }

        private static IReadOnlyList<string>? Missing(Mod mod, HashSet<string> provided)
        {
            if (mod.RequiredMods is null || mod.RequiredMods.Length == 0)
                return null;

            var missing = mod.RequiredMods
                .Where(id => !ModDependencyReader.IsProvidedByTheLoader(id) && !provided.Contains(id))
                .ToList();

            return missing.Count == 0 ? null : missing;
        }
    }
}
