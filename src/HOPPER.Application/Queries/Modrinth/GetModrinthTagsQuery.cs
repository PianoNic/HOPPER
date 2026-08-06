using HOPPER.Application.Dtos.Modrinth;
using HOPPER.Application.Mappings.Modrinth;
using HOPPER.Application.Modrinth;
using Mediator;

namespace HOPPER.Application.Queries.Modrinth
{
    /// <summary>The loader and Minecraft version lists behind the browser's two filter dropdowns.
    /// Both are cached upstream in the client for six hours - they change when Mojang or a loader
    /// ships, which is not something a dashboard needs to learn within the minute.</summary>
    public record GetModrinthTagsQuery : IQuery<ModrinthTagsDto>;

    public class GetModrinthTagsQueryHandler(IModrinthClient modrinth) : IQueryHandler<GetModrinthTagsQuery, ModrinthTagsDto>
    {
        public async ValueTask<ModrinthTagsDto> Handle(GetModrinthTagsQuery query, CancellationToken cancellationToken)
        {
            var tags = await modrinth.GetTagsAsync(cancellationToken);
            return tags.ToDto();
        }
    }
}
