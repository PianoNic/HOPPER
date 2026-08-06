using HOPPER.Application.Dtos.Jar;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Queries.Jar
{
    /// <summary>Builds the client jar for one server. BaseUrl is passed in for the same reason the
    /// manifest query takes one: only the controller can see the request and its forwarded headers,
    /// and the URL baked into this jar is dialled from a player's machine, so it has to be the
    /// externally reachable one.</summary>
    public record GenerateLocatorJarQuery(Guid ServerId, string BaseUrl) : IQuery<LocatorJarDto>;

    public class GenerateLocatorJarQueryHandler(HopperDbContext db, ILocatorJarBuilder builder)
        : IQueryHandler<GenerateLocatorJarQuery, LocatorJarDto>
    {
        public async ValueTask<LocatorJarDto> Handle(GenerateLocatorJarQuery query, CancellationToken cancellationToken)
        {
            var server = await db.Servers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == query.ServerId, cancellationToken)
                ?? throw new ServerNotFoundException(query.ServerId);

            // The manifest path carries no server segment - the token identifies the server - so every
            // server's jar points at the same URL and differs only in the token beside it.
            var manifestUrl = $"{query.BaseUrl.TrimEnd('/')}/api/manifest";

            return new LocatorJarDto
            {
                FileName = $"{server.Slug}-hopper.jar",

                // The loader and Minecraft version pick which adapter is copied. A loader resolves one
                // jar out of mods/ and ignores every other, so this is the difference between a client
                // that syncs and a file that sits there.
                Content = builder.Build(server.Id, manifestUrl, server.Token, server.Loader, server.MinecraftVersion),
            };
        }
    }
}
