using HOPPER.API.Auth;
using HOPPER.Application.Command.Clients;
using HOPPER.Application.Dtos.Clients;
using HOPPER.Application.Queries.Clients;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    /// <summary>Two audiences on one route prefix: List is the dashboard's (OIDC, via the fallback
    /// policy) and Report is the game client's (shared token). The explicit [Authorize] on Report
    /// replaces the fallback policy for that action only, so the two never overlap.</summary>
    [ApiController]
    [Route("api/clients")]
    public class ClientsController(IMediator mediator) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<ClientDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new ListClientsQuery(), cancellationToken);
            return Ok(result);
        }

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
                await mediator.Send(new RecordClientReportCommand(body, ip), cancellationToken);
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}
