using System.Security.Claims;

namespace HOPPER.API.Auth
{
    public static class ClientPrincipalExtensions
    {
        /// <summary>The server the caller's bearer token resolved to.
        ///
        /// Throws rather than returning null or Guid.Empty: this is only ever called from an action
        /// the ClientToken scheme already authenticated, so a missing claim means the handler and the
        /// controller have drifted apart. Failing loudly turns that into a 500 in development instead
        /// of a query filtered on Guid.Empty that quietly returns an empty manifest in production.</summary>
        public static Guid ServerId(this ClaimsPrincipal principal) =>
            Guid.TryParse(principal.FindFirstValue(ClientTokenDefaults.ServerIdClaim), out var id)
                ? id
                : throw new InvalidOperationException("Authenticated client carries no server id.");
    }
}
