using HOPPER.API.Auth;
using HOPPER.Application.Command.Clients;
using HOPPER.Application.Dtos.Clients;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    /// <summary>The game client's inventory report. The path is fixed by the already-shipped Java
    /// client, which derives it as manifestUrl.resolve("clients/report") - so it stays at
    /// /api/clients/report and carries no server segment. The admin's view of the same data lives on
    /// ServerClientsController instead.</summary>
    [ApiController]
    [Route("api/clients")]
    public class ClientsController(IMediator mediator) : ControllerBase
    {
        [HttpPost("report")]
        [Authorize(AuthenticationSchemes = ClientTokenDefaults.AuthenticationScheme)]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Report([FromBody] ClientReportDto body, CancellationToken cancellationToken = default)
        {
            try
            {
                var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
                await mediator.Send(new RecordClientReportCommand(User.ServerId(), body, ip), cancellationToken);
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
