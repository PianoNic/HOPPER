using HOPPER.Application.Dtos.Modrinth;
using HOPPER.Application.Mappings.Modrinth;
using HOPPER.Application.Modrinth;
using Mediator;

namespace HOPPER.Application.Queries.Modrinth
{
    /// <summary>One project's detail panel. Takes an id or a slug because both resolve on the same
    /// upstream path, and the dashboard has whichever the last response happened to carry.</summary>
    public record GetModrinthProjectQuery(string IdOrSlug) : IQuery<ModrinthProjectDto>;

    public class GetModrinthProjectQueryHandler(IModrinthClient modrinth)
        : IQueryHandler<GetModrinthProjectQuery, ModrinthProjectDto>
    {
        public async ValueTask<ModrinthProjectDto> Handle(GetModrinthProjectQuery query, CancellationToken cancellationToken)
        {
            var project = await modrinth.GetProjectAsync(query.IdOrSlug, cancellationToken);
            return project.ToDto();
        }
    }
}
