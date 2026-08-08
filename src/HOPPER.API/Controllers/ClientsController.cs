using HOPPER.API.Auth;
using HOPPER.Application.Command.Clients;
using HOPPER.Application.Dtos.Clients;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    [ApiController]
    [Route("api/clients")]
    public class ClientsController(IMediator mediator) : ControllerBase
    {
        [HttpPost("report")]
        [Authorize(AuthenticationSchemes = ClientTokenDefaults.AuthenticationScheme)]
        [RequestSizeLimit(1024 * 1024)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Report([FromBody] ClientReportDto body, CancellationToken cancellationToken = default)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            await mediator.Send(new RecordClientReportCommand(User.ServerId(), body, ip), cancellationToken);
            return NoContent();
        }
    }
}
