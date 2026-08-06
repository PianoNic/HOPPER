using HOPPER.Application.Dtos.Servers;
using HOPPER.Application.Mappings.Servers;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Servers
{
    public record GetServerTokenQuery(Guid Id) : IQuery<ServerTokenDto>;

    public class GetServerTokenQueryHandler(HopperDbContext db) : IQueryHandler<GetServerTokenQuery, ServerTokenDto>
    {
        public async ValueTask<ServerTokenDto> Handle(GetServerTokenQuery query, CancellationToken cancellationToken)
        {
            var server = await db.Servers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == query.Id, cancellationToken)
                ?? throw new ServerNotFoundException(query.Id);

            return server.ToTokenDto();
        }
    }
}
