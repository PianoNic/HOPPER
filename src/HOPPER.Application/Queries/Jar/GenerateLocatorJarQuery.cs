using HOPPER.Application.Dtos.Jar;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Jar
{
    public record GenerateLocatorJarQuery(Guid ServerId, string BaseUrl) : IQuery<LocatorJarDto>;

    public class GenerateLocatorJarQueryHandler(HopperDbContext db, ILocatorJarBuilder builder)
        : IQueryHandler<GenerateLocatorJarQuery, LocatorJarDto>
    {
        public async ValueTask<LocatorJarDto> Handle(GenerateLocatorJarQuery query, CancellationToken cancellationToken)
        {
            var server = await db.Servers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == query.ServerId, cancellationToken)
                ?? throw new ServerNotFoundException(query.ServerId);

            var manifestUrl = $"{query.BaseUrl.TrimEnd('/')}/api/manifest";

            return new LocatorJarDto
            {
                FileName = $"{server.Slug}-hopper.jar",

                Content = builder.Build(server.Id, manifestUrl, server.Token, server.Loader, server.MinecraftVersion),
            };
        }
    }
}
