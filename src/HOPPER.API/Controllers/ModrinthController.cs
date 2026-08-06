using HOPPER.Application.Dtos.Modrinth;
using HOPPER.Application.Modrinth;
using HOPPER.Application.Queries.Modrinth;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    [ApiController]
    [Route("api/modrinth")]
    public class ModrinthController(IMediator mediator) : ControllerBase
    {
        [HttpGet("search")]
        [ProducesResponseType(typeof(ModrinthSearchResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> Search(
            [FromQuery] string? query = null,
            [FromQuery] string? loader = null,
            [FromQuery] string? gameVersion = null,
            [FromQuery] ModrinthSearchIndex index = ModrinthSearchIndex.Relevance,
            [FromQuery] int offset = 0,
            [FromQuery] int limit = 20,
            [FromQuery] Guid? serverId = null,
            CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(
                new SearchModrinthQuery(serverId, query, loader, gameVersion, index, offset, limit), cancellationToken);

            return Ok(result);
        }

        [HttpGet("projects/{idOrSlug}")]
        [ProducesResponseType(typeof(ModrinthProjectDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> Project(string idOrSlug, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new GetModrinthProjectQuery(idOrSlug), cancellationToken);
            return Ok(result);
        }

        [HttpGet("projects/{idOrSlug}/versions")]
        [ProducesResponseType(typeof(IReadOnlyList<ModrinthVersionDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> Versions(
            string idOrSlug,
            [FromQuery] string? loader = null,
            [FromQuery] string? gameVersion = null,
            [FromQuery] Guid? serverId = null,
            CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(
                new ListModrinthVersionsQuery(serverId, idOrSlug, loader, gameVersion), cancellationToken);

            return Ok(result);
        }

        [HttpGet("tags")]
        [ProducesResponseType(typeof(ModrinthTagsDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> Tags(CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new GetModrinthTagsQuery(), cancellationToken);
            return Ok(result);
        }
    }
}
