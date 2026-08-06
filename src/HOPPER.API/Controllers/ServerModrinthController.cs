using HOPPER.Application.Command.Modrinth;
using HOPPER.Application.Dtos.Modrinth;
using HOPPER.Application.Queries.Modrinth;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    [ApiController]
    [Route("api/servers/{id:guid}/modrinth")]
    public class ServerModrinthController(IMediator mediator) : ControllerBase
    {
        [HttpPost("plan")]
        [ProducesResponseType(typeof(ModrinthInstallPlanDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> Plan(
            Guid id, [FromBody] ModrinthPlanRequest body, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(
                new PlanModrinthInstallQuery(id, body.VersionIds ?? [], body.OptionalVersionIds ?? []),
                cancellationToken);

            return Ok(result);
        }

        [HttpPost("install")]
        [ProducesResponseType(typeof(ModrinthInstallResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        [ProducesResponseType(StatusCodes.Status502BadGateway)]
        public async Task<IActionResult> Install(
            Guid id, [FromBody] ModrinthInstallRequest body, CancellationToken cancellationToken = default)
        {
            var items = (body.Items ?? [])
                .Select(i => new ModrinthInstallItem(i.VersionId, i.Replace))
                .ToList();

            var result = await mediator.Send(new InstallModrinthModsCommand(id, items), cancellationToken);
            return Ok(result);
        }
    }

    public record ModrinthPlanRequest(IReadOnlyList<string>? VersionIds, IReadOnlyList<string>? OptionalVersionIds);

    public record ModrinthInstallRequest(IReadOnlyList<ModrinthInstallItemDto>? Items);
}
