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
            // One stat per distinct hash rather than per row: blobs are shared, so a server that
            // lists the same jar twice under two names would otherwise pay for it twice.
            var present = rows.Select(m => m.Sha256).Distinct(StringComparer.Ordinal)
                .ToDictionary(sha => sha, blobs.Exists, StringComparer.Ordinal);

            var collisions = ModIdConflictValidator.Collisions(rows);

            return rows
                .Select(m => m.ToDto() with
                {
                    BytesMissing = !present[m.Sha256],
                    // TryGetValue, not GetValueOrDefault: the default of a non-nullable enum is
                    // SyncSide.Client, so every mod that collides with nothing would claim it does.
                    CollidesOn = collisions.TryGetValue(m.Id, out var side) ? side : null,
                })
                .ToList();
        }
    }
}
