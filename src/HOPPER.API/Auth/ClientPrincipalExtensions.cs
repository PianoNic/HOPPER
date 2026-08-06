using System.Security.Claims;

namespace HOPPER.API.Auth
{
    public static class ClientPrincipalExtensions
    {
        public static Guid ServerId(this ClaimsPrincipal principal) =>
            Guid.TryParse(principal.FindFirstValue(ClientTokenDefaults.ServerIdClaim), out var id)
                ? id
                : throw new InvalidOperationException("Authenticated client carries no server id.");
    }
}
