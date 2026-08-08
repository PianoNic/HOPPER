using HOPPER.Application.Command.Mods;
using HOPPER.Application.Dtos.Mods;
using HOPPER.Application.Queries.Mods;
using HOPPER.Domain.Enums;
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

        [HttpPatch("side")]
        [ProducesResponseType(typeof(SetModSideResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SetSide(Guid id, [FromBody] SetModSideRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null || request.ModIds is null || request.ModIds.Count == 0)
                return BadRequest(new { error = "No mods named." });

            var updated = await mediator.Send(new SetModSideCommand(id, request.ModIds, request.Side), cancellationToken);
            return Ok(new SetModSideResultDto { Updated = updated });
        }

        [HttpPost("delete")]
        [ProducesResponseType(typeof(DeleteModsResultDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> DeleteMany(Guid id, [FromBody] DeleteModsRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null || request.ModIds is null || request.ModIds.Count == 0)
                return BadRequest(new { error = "No mods named." });

            var deleted = await mediator.Send(new DeleteModsCommand(id, request.ModIds), cancellationToken);
            return Ok(new DeleteModsResultDto { Deleted = deleted });
        }

    }

    public record SetModSideRequest(IReadOnlyList<Guid> ModIds, ModSide Side);

    public record DeleteModsRequest(IReadOnlyList<Guid> ModIds);
}
