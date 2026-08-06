using HOPPER.Application.Dtos.Servers;
using HOPPER.Application.Mappings.Servers;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Servers
{
    public record GetServerQuery(Guid Id) : IQuery<ServerDto>;

    public class GetServerQueryHandler(HopperDbContext db) : IQueryHandler<GetServerQuery, ServerDto>
    {
        public async ValueTask<ServerDto> Handle(GetServerQuery query, CancellationToken cancellationToken)
        {
            var server = await db.Servers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == query.Id, cancellationToken)
                ?? throw new ServerNotFoundException(query.Id);

            var modCount = await db.Mods.CountAsync(m => m.ServerId == query.Id, cancellationToken);
            var clientCount = await db.Clients.CountAsync(c => c.ServerId == query.Id, cancellationToken);

            return server.ToDto(modCount, clientCount);
        }
    }
}
