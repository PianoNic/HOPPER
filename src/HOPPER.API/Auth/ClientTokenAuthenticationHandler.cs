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
    /// <summary>Authenticates the Forge locator against a per-server bearer token. The locator is a
    /// jar sitting in a player's mods folder: it cannot run an OIDC code flow, and a per-player
    /// credential would have to be minted and pasted by hand for every friend. One rotatable token
    /// per server is the right trade at this scale - and it is what the generated jar carries, so a
    /// player configures nothing.
    ///
    /// The token is the tenant boundary: it does not merely say "you are a client", it says WHICH
    /// server's client you are, and that answer is minted as a claim here so no downstream endpoint
    /// has to trust a server id from a URL or a request body.
    ///
    /// Unlike KRINT's node tokens the value is not hashed, because HOPPER has to be able to read it
    /// back to write it into a downloaded jar. See Server.Token.</summary>
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

            // The whole column, not a WHERE on the token. A parameterised equality lookup in Postgres
            // is not constant time, so pushing the comparison down would leak the token through
            // response timing exactly as a plain string == would. A HOPPER instance has a handful of
            // servers, so this is one small indexed read and the fixed-time compare below stays the
            // only comparison that happens.
            var servers = await db.Servers.AsNoTracking()
                .Select(s => new { s.Id, s.Token })
                .ToListAsync(Context.RequestAborted);

            if (servers.Count == 0)
            {
                // No servers means no valid tokens. This locks the door rather than opening it;
                // getting it backwards would publish every mod set on a fresh install.
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
                    // Deliberately no early return: bailing out here would make the response time
                    // depend on which server matched, which is exactly what the fixed-time compare
                    // is here to avoid.
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
