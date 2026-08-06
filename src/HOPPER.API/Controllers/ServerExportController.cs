using HOPPER.Application.Queries.Exports;
using HOPPER.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    [ApiController]
    [Route("api/servers/{id:guid}/export")]
    public class ServerExportController(IMediator mediator) : ControllerBase
    {
        [HttpGet]

        [Produces("application/octet-stream")]
        [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Export(
            Guid id, [FromQuery] PackFormat format = PackFormat.Modrinth, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new ExportServerPackQuery(id, format), cancellationToken);

            if (result.Warnings.Count > 0)
            {
                var joined = string.Join(" | ", result.Warnings)
                    .Replace('\r', ' ')
                    .Replace('\n', ' ');

                Response.Headers["X-Hopper-Export-Warnings"] =
                    joined.Length > 1000 ? joined[..1000] : joined;
            }

            return File(result.Content, result.ContentType, result.FileName);
        }
    }
}
