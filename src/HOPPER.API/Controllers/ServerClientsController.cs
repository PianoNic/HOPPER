using HOPPER.Application.Dtos.Clients;
using HOPPER.Application.Queries.Clients;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    /// <summary>Admin surface for one server's known clients. Kept apart from ClientsController so
    /// that the client-facing report route keeps its own path and its own auth scheme.</summary>
    [ApiController]
    [Route("api/servers/{id:guid}/clients")]
    public class ServerClientsController(IMediator mediator) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<ClientDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(Guid id, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new ListClientsQuery(id), cancellationToken);
            return Ok(result);
        }
    }
}
