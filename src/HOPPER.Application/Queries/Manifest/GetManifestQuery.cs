using HOPPER.Application.Dtos.Manifest;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Manifest
{
    public record GetManifestQuery(Guid ServerId, string BaseUrl, SyncSide Side = SyncSide.Client) : IQuery<ManifestDto>;

    public class GetManifestQueryHandler(HopperDbContext db) : IQueryHandler<GetManifestQuery, ManifestDto>
    {
        public async ValueTask<ManifestDto> Handle(GetManifestQuery query, CancellationToken cancellationToken)
        {
            var baseUrl = query.BaseUrl.TrimEnd('/');

            var sideQuery = query.Side == SyncSide.Server ? "?side=server" : string.Empty;

            var rows = await db.Mods.AsNoTracking()
                .Where(m => m.ServerId == query.ServerId)
                .Where(ModSideRules.ReachesExpression(query.Side))
                .OrderBy(m => m.FileName)
                .ToListAsync(cancellationToken);

            return new ManifestDto
            {
                Mods = rows.Select(m => new ManifestModDto
                {
                    File = m.FileName,
                    Url = $"{baseUrl}/api/blobs/{m.Sha256}{sideQuery}",
                    Sha256 = m.Sha256,
                    Size = m.Size,

                    ModIds = m.ModIds is { Length: > 0 } ids ? ids : null,
                }).ToList(),
            };
        }
    }
}
