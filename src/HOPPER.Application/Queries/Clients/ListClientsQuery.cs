using HOPPER.Application.Dtos.Clients;
using HOPPER.Application.Mappings.Clients;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Clients
{
    public record ListClientsQuery : IQuery<IReadOnlyList<ClientDto>>;

    public class ListClientsQueryHandler(HopperDbContext db) : IQueryHandler<ListClientsQuery, IReadOnlyList<ClientDto>>
    {
        public async ValueTask<IReadOnlyList<ClientDto>> Handle(ListClientsQuery query, CancellationToken cancellationToken)
        {
            var clients = await db.Clients.AsNoTracking()
                .OrderByDescending(c => c.LastSeenAt)
                .ToListAsync(cancellationToken);

            // Three flat reads and an in-memory grouping. At friend-group scale the reported-mod table
            // is a few hundred rows, so pulling it whole beats one query per client (N+1) by a wide
            // margin and keeps the "known" check to a single set lookup.
            var reported = await db.ClientReportedMods.AsNoTracking().ToListAsync(cancellationToken);
            var knownHashes = (await db.Mods.AsNoTracking().Select(m => m.Sha256).ToListAsync(cancellationToken))
                .ToHashSet(StringComparer.Ordinal);

            var byClient = reported
                .GroupBy(r => r.ClientId)
                .ToDictionary(g => g.Key, g => g.OrderBy(r => r.FileName).ToList());

            return clients.Select(c =>
            {
                var mods = byClient.TryGetValue(c.Id, out var rows)
                    ? rows.Select(r => new ClientModDto
                    {
                        FileName = r.FileName,
                        Sha256 = r.Sha256,
                        Known = knownHashes.Contains(r.Sha256),
                    }).ToList()
                    : new List<ClientModDto>();

                return c.ToDto(mods);
            }).ToList();
        }
    }
}
