using HOPPER.Application.Dtos.Loaders;
using HOPPER.Application.Queries.Loaders;
using HOPPER.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    [ApiController]
    [Route("api/loaders")]
    public class LoadersController(IMediator mediator) : ControllerBase
    {
        [HttpGet("{loader}/versions")]
        [ProducesResponseType(typeof(IReadOnlyList<LoaderVersionDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> Versions(
            ModLoader loader,
            [FromQuery] string? minecraftVersion = null,
            CancellationToken cancellationToken = default)
        {
            var versions = await mediator.Send(new ListLoaderVersionsQuery(loader, minecraftVersion), cancellationToken);
            return Ok(versions);
        }
    }
}
