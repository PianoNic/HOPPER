using HOPPER.Application.Dtos.Clients;
using HOPPER.Application.Mappings.Clients;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Clients
{
    public record ListClientsQuery(Guid ServerId) : IQuery<IReadOnlyList<ClientDto>>;

    public class ListClientsQueryHandler(HopperDbContext db) : IQueryHandler<ListClientsQuery, IReadOnlyList<ClientDto>>
    {
        public async ValueTask<IReadOnlyList<ClientDto>> Handle(ListClientsQuery query, CancellationToken cancellationToken)
        {
            var clients = await db.Clients.AsNoTracking()
                .Where(c => c.ServerId == query.ServerId)
                .OrderByDescending(c => c.LastSeenAt)
                .ToListAsync(cancellationToken);

            var clientIds = clients.Select(c => c.Id).ToHashSet();
            var reported = await db.ClientReportedMods.AsNoTracking()
                .Where(r => clientIds.Contains(r.ClientId))
                .ToListAsync(cancellationToken);

            var knownHashes = (await db.Mods.AsNoTracking()
                    .Where(m => m.ServerId == query.ServerId)
                    .Select(m => m.Sha256)
                    .ToListAsync(cancellationToken))
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
