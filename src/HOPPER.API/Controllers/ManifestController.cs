using HOPPER.API.Auth;
using HOPPER.Application.Dtos.Manifest;
using HOPPER.Application.Queries.Manifest;
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
        public async Task<IActionResult> Get(CancellationToken cancellationToken = default)
        {
            var baseUrl = configuration["Hopper:PublicBaseUrl"] is { Length: > 0 } configured
                ? configured
                : $"{Request.Scheme}://{Request.Host}";

            var result = await mediator.Send(new GetManifestQuery(User.ServerId(), baseUrl), cancellationToken);

            return Ok(result);
        }
    }
}
