using HOPPER.Application.Dtos.Modrinth;
using HOPPER.Application.Mappings.Modrinth;
using HOPPER.Application.Modrinth;
using Mediator;

namespace HOPPER.Application.Queries.Modrinth
{
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
