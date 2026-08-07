using HOPPER.API.Auth;
using HOPPER.Application.Dtos.Manifest;
using HOPPER.Application.Queries.Manifest;
using HOPPER.Domain.Enums;
using Mediator;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    [ApiController]
    [Route("api/manifest")]
    [Authorize(AuthenticationSchemes = ClientTokenDefaults.AuthenticationScheme)]
    public class ManifestController(IMediator mediator, IConfiguration configuration) : ControllerBase
    {
        [HttpGet]
        [ProducesResponseType(typeof(ManifestDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Get([FromQuery] string? side = null, CancellationToken cancellationToken = default)
        {
            // A dedicated server quietly receiving the client set is the failure this whole feature
            // exists to prevent, so an unrecognised value is refused rather than defaulted.
            if (!ModSideRules.TryParse(side, out var syncSide))
                return BadRequest(new { error = "side must be 'client' or 'server'." });

            var baseUrl = configuration["Hopper:PublicBaseUrl"] is { Length: > 0 } configured
                ? configured
                : $"{Request.Scheme}://{Request.Host}";

            var result = await mediator.Send(new GetManifestQuery(User.ServerId(), baseUrl, syncSide), cancellationToken);

            return Ok(result);
        }
    }
}
