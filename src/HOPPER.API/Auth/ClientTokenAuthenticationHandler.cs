using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using HOPPER.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HOPPER.API.Auth
{
    public class ClientTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        HopperDbContext db)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        private const string BearerPrefix = "Bearer ";

        protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (string.IsNullOrEmpty(header) || !header.StartsWith(BearerPrefix, StringComparison.Ordinal))
                return AuthenticateResult.NoResult();

            var presented = header[BearerPrefix.Length..].Trim();
            if (presented.Length == 0)
                return AuthenticateResult.NoResult();

            var servers = await db.Servers.AsNoTracking()
                .Select(s => new { s.Id, s.Token })
                .ToListAsync(Context.RequestAborted);

            if (servers.Count == 0)
            {
                return AuthenticateResult.Fail("No servers configured.");
            }

            var presentedBytes = Encoding.UTF8.GetBytes(presented);
            Guid? matched = null;

            foreach (var server in servers)
            {
                if (string.IsNullOrEmpty(server.Token))
                    continue;

                var candidateBytes = Encoding.UTF8.GetBytes(server.Token);
                if (candidateBytes.Length == presentedBytes.Length
                    && CryptographicOperations.FixedTimeEquals(presentedBytes, candidateBytes))
                {
                    matched = server.Id;
                }
            }

            if (matched is null)
                return AuthenticateResult.Fail("Unknown client token.");

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Name, "hopper-client"),
                    new Claim(ClientTokenDefaults.ServerIdClaim, matched.Value.ToString()),
                ],
                ClientTokenDefaults.AuthenticationScheme);

            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), ClientTokenDefaults.AuthenticationScheme);
            return AuthenticateResult.Success(ticket);
        }
    }
}
