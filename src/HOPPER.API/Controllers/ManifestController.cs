using HOPPER.API.Auth;
using HOPPER.Application.Dtos.Manifest;
using HOPPER.Application.Queries.Manifest;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    /// <summary>The one endpoint the Forge locator polls on every launch. Its response shape is a
    /// fixed contract with the already-shipped Java client — see ManifestDto.</summary>
    [ApiController]
    [Route("api/manifest")]
    [Authorize(AuthenticationSchemes = ClientTokenDefaults.AuthenticationScheme)]
    public class ManifestController(IMediator mediator, IConfiguration configuration) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(ManifestDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Get(CancellationToken cancellationToken = default)
        {
            // The URLs go into a manifest consumed by a game client on another machine, so they must
            // be absolute and externally reachable. Request.Scheme/Host already reflect
            // X-Forwarded-Proto and X-Forwarded-Host because UseForwardedHeaders runs first, which
            // covers the ordinary reverse-proxy case; Hopper:PublicBaseUrl is the escape hatch for a
            // proxy that does not send them or a host that differs from the one clients dial.
            var baseUrl = configuration["Hopper:PublicBaseUrl"] is { Length: > 0 } configured
                ? configured
                : $"{Request.Scheme}://{Request.Host}";

            var result = await mediator.Send(new GetManifestQuery(baseUrl), cancellationToken);

            // Ok(dto) writes the DTO as the entire body: {"mods":[...]} with no envelope.
            return Ok(result);
        }
    }
}
