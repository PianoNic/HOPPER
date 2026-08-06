using HOPPER.Application.Command.Mods;
using HOPPER.Application.Dtos.Mods;
using HOPPER.Application.Queries.Mods;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    [ApiController]
    [Route("api/servers/{id:guid}/mods")]
    public class ServerModsController(IMediator mediator) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<ModDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(Guid id, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new ListModsQuery(id), cancellationToken);
            return Ok(result);
        }

        [HttpPost]

        [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
        [ProducesResponseType(typeof(ModUploadResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Upload(Guid id, IFormFileCollection files, CancellationToken cancellationToken = default)
        {
            if (files is null || files.Count == 0)
                return BadRequest(new { error = "No files uploaded." });

            var opened = new List<Stream>(files.Count);
            try
            {
                var batch = new List<UploadFile>(files.Count);
                foreach (var file in files)
                {
                    var stream = file.OpenReadStream();
                    opened.Add(stream);
                    batch.Add(new UploadFile(file.FileName, stream));
                }

                var result = await mediator.Send(new UploadModsCommand(id, batch), cancellationToken);
                return Ok(result);
            }
            finally
            {
                foreach (var stream in opened)
                    await stream.DisposeAsync();
            }
        }

        [HttpDelete("{modId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Delete(Guid id, Guid modId, CancellationToken cancellationToken = default)
        {
            await mediator.Send(new DeleteModCommand(id, modId), cancellationToken);
            return NoContent();
        }
    }
}
