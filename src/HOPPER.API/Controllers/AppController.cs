using System.Reflection;
using HOPPER.API.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HOPPER.API.Controllers
{
    /// <summary>Bootstrap configuration for the dashboard. Anonymous by necessity — the SPA calls it
    /// before it has a token, because this is what tells it where to get one.</summary>
    [ApiController]
    [Route("api/app")]
    public class AppController(IConfiguration configuration) : ControllerBase
    {
        // /application.properties at the repo root is the single source of truth for the version;
        // src/Directory.Build.props XmlPeeks it into AssemblyInformationalVersion at build time.
        // SourceLink may append "+<commit>", so strip it and show a clean semver.
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
            // Fall back to the request's own origin when no redirect URI is configured, so a
            // same-origin deployment needs no OIDC redirect settings at all. An explicitly configured
            // value always wins: behind a TLS-terminating proxy the derived one can be http:// even
            // when the admin set the right https:// URL, and the IdP would reject it.
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
