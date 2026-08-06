using HOPPER.Application.Command.Imports;
using HOPPER.Application.Dtos.Imports;
using HOPPER.Application.Dtos.Mods;
using HOPPER.Application.Queries.Imports;
using HOPPER.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    [ApiController]
    [Route("api/servers/{id:guid}")]
    public class ServerImportsController(IMediator mediator) : ControllerBase
    {
        [HttpPost("imports")]

        [Consumes("multipart/form-data", "application/json")]
        [RequestSizeLimit(2L * 1024 * 1024 * 1024)]
        [ProducesResponseType(typeof(ModImportDto), StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Start(Guid id, IFormFile? file, CancellationToken cancellationToken = default)
        {
            if (file is not null && file.Length > 0)
            {
                await using var stream = file.OpenReadStream();
                var uploaded = await mediator.Send(
                    new StartPackImportCommand(id, ImportSourceKind.Upload, file.FileName, stream, null),
                    cancellationToken);

                return Accepted(uploaded);
            }

            var url = Request.HasFormContentType
                ? Request.Form["url"].ToString()
                : (await Request.ReadFromJsonAsync<ImportUrlRequest>(cancellationToken))?.Url;

            if (string.IsNullOrWhiteSpace(url))
                return BadRequest(new { error = "Upload a pack file or give a URL to import from." });

            var result = await mediator.Send(
                new StartPackImportCommand(id, ImportSourceKind.Url, url, null, url),
                cancellationToken);

            return Accepted(result);
        }

        [HttpGet("imports")]
        [ProducesResponseType(typeof(IReadOnlyList<ModImportDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(Guid id, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new ListImportsQuery(id), cancellationToken);
            return Ok(result);
        }

        [HttpGet("imports/{importId:guid}")]
        [ProducesResponseType(typeof(ModImportDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(Guid id, Guid importId, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new GetImportQuery(id, importId), cancellationToken);
            return Ok(result);
        }

        [HttpGet("pending")]
        [ProducesResponseType(typeof(IReadOnlyList<PendingModDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> ListPending(Guid id, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new ListPendingModsQuery(id), cancellationToken);
            return Ok(result);
        }

        [HttpPost("pending/{pendingId:guid}")]
        [RequestSizeLimit(512L * 1024 * 1024)]
        [ProducesResponseType(typeof(ModDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> ResolvePending(Guid id, Guid pendingId, IFormFile file, CancellationToken cancellationToken = default)
        {
            if (file is null || file.Length == 0)
                return BadRequest(new { error = "No file uploaded." });

            await using var stream = file.OpenReadStream();

            var result = await mediator.Send(
                new ResolvePendingModCommand(id, pendingId, file.FileName, stream),
                cancellationToken);

            return Ok(result);
        }

        [HttpDelete("pending/{pendingId:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> DeletePending(Guid id, Guid pendingId, CancellationToken cancellationToken = default)
        {
            await mediator.Send(new DeletePendingModCommand(id, pendingId), cancellationToken);
            return NoContent();
        }
    }

    public record ImportUrlRequest(string Url);
}
