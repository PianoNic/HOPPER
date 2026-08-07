using HOPPER.Application.Command.Servers;
using HOPPER.Application.Dtos.Servers;
using HOPPER.Application.Queries.Jar;
using HOPPER.Application.Queries.Servers;
using HOPPER.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    [ApiController]
    [Route("api/servers")]
    public class ServersController(IMediator mediator, IConfiguration configuration) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(IReadOnlyList<ServerDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> List(CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new ListServersQuery(), cancellationToken);
            return Ok(result);
        }

        [HttpGet("{id:guid}")]
        [ProducesResponseType(typeof(ServerDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new GetServerQuery(id), cancellationToken);
            return Ok(result);
        }

        [HttpPost]
        [ProducesResponseType(typeof(ServerDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Create([FromBody] CreateServerRequest body, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(
                new CreateServerCommand(body.Name, body.Slug, body.MinecraftVersion, body.Loader, body.LoaderVersion),
                cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }

        [HttpPut("{id:guid}")]
        [ProducesResponseType(typeof(ServerDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateServerRequest body, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(
                new UpdateServerCommand(id, body.Name, body.Slug, body.MinecraftVersion, body.Loader, body.LoaderVersion),
                cancellationToken);
            return Ok(result);
        }

        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
        {
            await mediator.Send(new DeleteServerCommand(id), cancellationToken);
            return NoContent();
        }

        [HttpGet("{id:guid}/token")]
        [ProducesResponseType(typeof(ServerTokenDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Token(Guid id, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new GetServerTokenQuery(id), cancellationToken);
            return Ok(result);
        }

        [HttpPost("{id:guid}/token")]
        [ProducesResponseType(typeof(ServerTokenDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RotateToken(Guid id, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new RotateServerTokenCommand(id), cancellationToken);
            return Ok(result);
        }

        [HttpGet("{id:guid}/jar")]
        [Produces("application/java-archive")]
        [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Jar(Guid id, [FromQuery] string? variant = null,
            CancellationToken cancellationToken = default)
        {
            var baseUrl = configuration["Hopper:PublicBaseUrl"] is { Length: > 0 } configured
                ? configured
                : $"{Request.Scheme}://{Request.Host}";

            var jar = await mediator.Send(new GenerateLocatorJarQuery(id, baseUrl, variant), cancellationToken);

            return File(jar.Content, "application/java-archive", jar.FileName);
        }
    }

    public record CreateServerRequest(
        string Name,
        string? Slug,
        string? MinecraftVersion = null,
        ModLoader Loader = ModLoader.Unknown,
        string? LoaderVersion = null);

    public record UpdateServerRequest(
        string Name,
        string Slug,
        string? MinecraftVersion = null,
        ModLoader Loader = ModLoader.Unknown,
        string? LoaderVersion = null);
}
