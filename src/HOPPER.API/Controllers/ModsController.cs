using HOPPER.Application;
using HOPPER.Application.Command.Mods;
using HOPPER.Application.Dtos.Mods;
using HOPPER.Application.Queries.Mods;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    /// <summary>Admin surface for the distributed mod set. No [Authorize] attribute: the fallback
    /// policy already requires an authenticated user, so an endpoint is protected by doing nothing.</summary>
    [ApiController]
    [Route("api/mods")]
    public class ModsController(IMediator mediator) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<ModDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new ListModsQuery(), cancellationToken);
            return Ok(result);
        }

        [HttpPost]
        // Forge content mods run to tens of megabytes; the framework's 30 MB default would reject the
        // larger ones with an opaque 413 that looks like a network problem.
        [RequestSizeLimit(512L * 1024 * 1024)]
        [ProducesResponseType(typeof(ModDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Upload(IFormFile file, CancellationToken cancellationToken = default)
        {
            if (file is null || file.Length == 0)
                return BadRequest(new { error = "No file uploaded." });

            try
            {
                await using var stream = file.OpenReadStream();
                var result = await mediator.Send(new UploadModCommand(file.FileName, stream), cancellationToken);
                return CreatedAtAction(nameof(List), new { id = result.Id }, result);
            }
            catch (DuplicateModFileNameException ex)
            {
                return Conflict(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
        {
            await mediator.Send(new DeleteModCommand(id), cancellationToken);
            return NoContent();
        }
    }
}
