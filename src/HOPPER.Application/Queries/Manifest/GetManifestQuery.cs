using HOPPER.Application.Dtos.Manifest;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Manifest
{
    /// <summary>Builds the fixed wire format the Forge locator consumes. BaseUrl is passed in rather
    /// than read from configuration here so the Application layer stays free of "how do I know my own
    /// externally reachable hostname" logic; the controller owns that, since only it can see the
    /// request and its forwarded headers.
    ///
    /// ServerId comes from the bearer token the caller presented, never from the URL: the shipped
    /// Java client derives its report URL from the manifest URL, so moving a server segment into
    /// the path would silently relocate POST /api/clients/report too.</summary>
    public record GetManifestQuery(Guid ServerId, string BaseUrl) : IQuery<ManifestDto>;

    public class GetManifestQueryHandler(HopperDbContext db) : IQueryHandler<GetManifestQuery, ManifestDto>
    {
        public async ValueTask<ManifestDto> Handle(GetManifestQuery query, CancellationToken cancellationToken)
        {
            var baseUrl = query.BaseUrl.TrimEnd('/');

            // Ordered by filename so two manifests taken at different times diff meaningfully, and so
            // a client comparing responses never sees a spurious change from row ordering alone.
            var rows = await db.Mods.AsNoTracking()
                .Where(m => m.ServerId == query.ServerId)
                .OrderBy(m => m.FileName)
                .ToListAsync(cancellationToken);

            return new ManifestDto
            {
                Mods = rows.Select(m => new ManifestModDto
                {
                    File = m.FileName,
                    Url = $"{baseUrl}/api/blobs/{m.Sha256}",
                    Sha256 = m.Sha256,
                    Size = m.Size,

                    // Both "never read" (null) and "declares nothing" (empty) collapse to an absent
                    // key. A client has nothing to do with either, and omitting keeps the entry
                    // byte-identical to the original four-field shape for every jar that is not a
                    // mod - which is what lets this field be added without touching a single one of
                    // the wire-format tests.
                    ModIds = m.ModIds is { Length: > 0 } ids ? ids : null,
                }).ToList(),
            };
        }
    }
}
