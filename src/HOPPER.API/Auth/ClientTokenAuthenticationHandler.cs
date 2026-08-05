using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace HOPPER.API.Auth
{
    /// <summary>Authenticates the Forge locator against a shared bearer token from configuration.
    /// The locator is a jar sitting in a player's mods folder: it cannot run an OIDC code flow, and
    /// a per-player credential would have to be minted and pasted by hand for every friend. One
    /// rotatable shared secret is the right trade at this scale.
    ///
    /// Unlike KRINT's node tokens the value is not hashed and stored per row, because there is no
    /// per-client identity to hang a hash on — a client's identity is the clientId it reports, which
    /// is an inventory key, not a credential.</summary>
    public class ClientTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IConfiguration configuration)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        private const string BearerPrefix = "Bearer ";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var header = Request.Headers.Authorization.ToString();
            if (string.IsNullOrEmpty(header) || !header.StartsWith(BearerPrefix, StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());

            var presented = header[BearerPrefix.Length..].Trim();
            if (presented.Length == 0)
                return Task.FromResult(AuthenticateResult.NoResult());

            // An array so a token can be rotated without downtime: add the new one, redistribute, then
            // drop the old one.
            var allowed = configuration.GetSection("Hopper:ClientTokens").Get<string[]>() ?? [];
            if (allowed.Length == 0)
            {
                // An unconfigured allow-list locks the door rather than opening it. Getting this
                // backwards would publish the whole mod set to the internet on a fresh install.
                return Task.FromResult(AuthenticateResult.Fail("No client tokens configured."));
            }

            if (!IsAllowed(presented, allowed))
                return Task.FromResult(AuthenticateResult.Fail("Invalid client token."));

            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "hopper-client")],
                ClientTokenDefaults.AuthenticationScheme);
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), ClientTokenDefaults.AuthenticationScheme);

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }

        // Constant-time comparison over the raw bytes. A plain string == returns as soon as two bytes
        // differ, which leaks the token one character at a time to anyone who can time the response.
        private static bool IsAllowed(string presented, string[] allowed)
        {
            var presentedBytes = Encoding.UTF8.GetBytes(presented);
            var match = false;

            foreach (var candidate in allowed)
            {
                if (string.IsNullOrEmpty(candidate))
                    continue;

                var candidateBytes = Encoding.UTF8.GetBytes(candidate);
                if (candidateBytes.Length == presentedBytes.Length
                    && CryptographicOperations.FixedTimeEquals(presentedBytes, candidateBytes))
                {
                    // Deliberately no early return: bailing out here would make the response time
                    // depend on which token matched, which is exactly what the fixed-time compare
                    // is here to avoid.
                    match = true;
                }
            }

            return match;
        }
    }
}
