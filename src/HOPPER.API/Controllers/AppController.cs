using System.Reflection;
using HOPPER.API.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    [ApiController]
    [Route("api/app")]
    public class AppController(IConfiguration configuration) : ControllerBase
    {
        private static readonly string AppVersion =
            typeof(AppController).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?.Split('+')[0]
            ?? "0.0.0";

        [AllowAnonymous]
        [HttpGet]
        [ProducesResponseType(typeof(AppDto), StatusCodes.Status200OK)]
        public IActionResult Get()
        {
            var origin = $"{Request.Scheme}://{Request.Host}/";
            var redirectUri = configuration["Oidc:RedirectUri"] is { Length: > 0 } configured ? configured : origin;

            return Ok(new AppDto(
                Authority: configuration["Oidc:Authority"] ?? string.Empty,
                ClientId: configuration["Oidc:ClientId"] ?? string.Empty,
                RedirectUri: redirectUri,
                PostLogoutRedirectUri: configuration["Oidc:PostLogoutRedirectUri"] ?? redirectUri,
                Scope: configuration["Oidc:Scope"] ?? "openid profile email roles",
                Version: AppVersion));
        }
    }
}
