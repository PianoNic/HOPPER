using HOPPER.Application.Dtos.Modrinth;
using HOPPER.Application.Modrinth;
using HOPPER.Application.Queries.Modrinth;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    /// <summary>The catalogue side of the mod browser. Admin surface: no [Authorize] attribute,
    /// because the fallback policy already requires an authenticated OIDC user and an endpoint here is
    /// protected by writing nothing. None of these is client-facing, so none goes into
    /// SecuritySchemeTransformer.ClientTokenPaths.
    ///
    /// Not routed under a server id, deliberately. Modrinth's catalogue is the same for every server,
    /// and scoping search under a tenant would mean refetching identical results per server for no
    /// gain. The optional serverId query parameter is only used to mark a hit as already installed.</summary>
    [ApiController]
    [Route("api/modrinth")]
    public class ModrinthController(IMediator mediator) : ControllerBase
    {
        /// <summary>Searches Modrinth for mods.
        ///
        /// The loader filter goes upstream as a CATEGORY facet, which is what that endpoint wants -
        /// the version listing below uses a separate loaders parameter instead. limit is clamped to
        /// 1..100 on this side because Modrinth clamp at 100 silently and echo the clamped value, and
        /// an unknown loader is refused rather than forwarded because an unknown facet comes back as
        /// zero hits rather than as an error.</summary>
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

        /// <summary>One project's detail. Takes a base62 id or a slug - both resolve upstream on the
        /// same path.</summary>
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

        /// <summary>A project's versions, newest first, already narrowed to the primary jar per
        /// version. Changelogs are never requested for a list - they are a third of the payload and
        /// are only read for the one version being inspected.</summary>
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

        /// <summary>Loader names and release Minecraft versions, for the browser's filter dropdowns.
        /// Cached upstream for six hours; both lists change when Mojang or a loader ships.</summary>
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
