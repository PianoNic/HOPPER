using HOPPER.Application;
using HOPPER.Application.Dtos.Loaders;
using HOPPER.Application.Loaders;
using HOPPER.Domain.Enums;
using Mediator;

namespace HOPPER.Application.Queries.Loaders
{
    public record ListLoaderVersionsQuery(ModLoader Loader, string? MinecraftVersion)
        : IQuery<IReadOnlyList<LoaderVersionDto>>;

    public class ListLoaderVersionsQueryHandler(LoaderVersionClient client)
        : IQueryHandler<ListLoaderVersionsQuery, IReadOnlyList<LoaderVersionDto>>
    {
        public async ValueTask<IReadOnlyList<LoaderVersionDto>> Handle(
            ListLoaderVersionsQuery query, CancellationToken cancellationToken)
        {
            if (!Enum.IsDefined(query.Loader))
                throw new InvalidRequestException($"Unknown loader: {(int)query.Loader}.");

            var versions = await client.GetAsync(query.Loader, query.MinecraftVersion, cancellationToken);

            return versions
                .Select(v => new LoaderVersionDto { Version = v.Version, Recommended = v.Recommended })
                .ToList();
        }
    }
}
