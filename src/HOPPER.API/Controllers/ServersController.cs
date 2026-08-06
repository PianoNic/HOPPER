using HOPPER.Application.Command.Servers;
using HOPPER.Application.Dtos.Servers;
using HOPPER.Application.Queries.Jar;
using HOPPER.Application.Queries.Servers;
using Mediator;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    /// <summary>The tenant CRUD. Entirely admin surface: no [Authorize] attribute anywhere here,
    /// because the fallback policy already requires an authenticated OIDC user and an endpoint is
    /// protected by doing nothing. A client token opens none of this.</summary>
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
            var result = await mediator.Send(new CreateServerCommand(body.Name, body.Slug), cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
        }

        [HttpPut("{id:guid}")]
        [ProducesResponseType(typeof(ServerDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status409Conflict)]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateServerRequest body, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new UpdateServerCommand(id, body.Name, body.Slug), cancellationToken);
            return Ok(result);
        }

        [HttpDelete("{id:guid}")]
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken = default)
        {
            await mediator.Send(new DeleteServerCommand(id), cancellationToken);
            return NoContent();
        }

        /// <summary>Reveals the server's bearer token. Its own route so it is never fetched by
        /// accident: the dashboard calls it behind an explicit "reveal" click, not on page load.</summary>
        [HttpGet("{id:guid}/token")]
        [ProducesResponseType(typeof(ServerTokenDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> Token(Guid id, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new GetServerTokenQuery(id), cancellationToken);
            return Ok(result);
        }

        /// <summary>Mints a new token and invalidates the old one immediately. Every jar already in a
        /// player's mods folder for this server stops working until it is downloaded again - the
        /// dashboard says exactly that before it calls this.</summary>
        [HttpPost("{id:guid}/token")]
        [ProducesResponseType(typeof(ServerTokenDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> RotateToken(Guid id, CancellationToken cancellationToken = default)
        {
            var result = await mediator.Send(new RotateServerTokenCommand(id), cancellationToken);
            return Ok(result);
        }

        /// <summary>The per-server client jar: a copy of the shipped template with this server's id,
        /// manifest URL and token written into hopper-server.properties inside it. A player drops it
        /// in mods/ and configures nothing.
        ///
        /// 503 rather than 500 when the template is missing, because that is a deployment that has not
        /// finished rather than a request that went wrong - and it is fixed by setting
        /// Hopper:LocatorTemplatePath, which the error body names.</summary>
        [HttpGet("{id:guid}/jar")]
        [Produces("application/java-archive")]
        [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> Jar(Guid id, CancellationToken cancellationToken = default)
        {
            // Same rule as ManifestController, and for the same reason: the URL is baked into a jar
            // that dials it from a player's machine, so it has to be the externally reachable one.
            // Request.Scheme/Host already carry the forwarded values because UseForwardedHeaders runs
            // first; Hopper:PublicBaseUrl is the escape hatch for a proxy that sends neither.
            var baseUrl = configuration["Hopper:PublicBaseUrl"] is { Length: > 0 } configured
                ? configured
                : $"{Request.Scheme}://{Request.Host}";

            var jar = await mediator.Send(new GenerateLocatorJarQuery(id, baseUrl), cancellationToken);

            // The bytes are already complete here - the builder finished the archive in memory - so
            // there is no path on which a half-patched jar reaches the player.
            return File(jar.Content, "application/java-archive", jar.FileName);
        }
    }

    /// <summary>Slug is optional: the dashboard asks for a name and lets HOPPER derive one.</summary>
    public record CreateServerRequest(string Name, string? Slug);

    public record UpdateServerRequest(string Name, string Slug);
}
