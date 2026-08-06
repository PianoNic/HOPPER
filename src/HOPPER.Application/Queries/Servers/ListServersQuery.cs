using HOPPER.Application.Dtos.Servers;
using HOPPER.Application.Mappings.Servers;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Servers
{
    public record ListServersQuery : IQuery<IReadOnlyList<ServerDto>>;

    public class ListServersQueryHandler(HopperDbContext db) : IQueryHandler<ListServersQuery, IReadOnlyList<ServerDto>>
    {
        public async ValueTask<IReadOnlyList<ServerDto>> Handle(ListServersQuery query, CancellationToken cancellationToken)
        {
            var servers = await db.Servers.AsNoTracking()
                .OrderBy(s => s.Name)
                .ToListAsync(cancellationToken);

            var modCounts = await db.Mods.AsNoTracking()
                .GroupBy(m => m.ServerId)
                .Select(g => new { ServerId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ServerId, x => x.Count, cancellationToken);

            var clientCounts = await db.Clients.AsNoTracking()
                .GroupBy(c => c.ServerId)
                .Select(g => new { ServerId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ServerId, x => x.Count, cancellationToken);

            return servers.Select(s => s.ToDto(
                modCounts.GetValueOrDefault(s.Id),
                clientCounts.GetValueOrDefault(s.Id))).ToList();
        }
    }
}
