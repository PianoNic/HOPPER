using Microsoft.AspNetCore.Authentication;

namespace HOPPER.API.Auth
{
    public static class ClientTokenExtensions
    {
        /// <summary>Registers the shared-token scheme alongside JWT bearer. It is never the default,
        /// so admin endpoints keep going through OIDC and a valid client token grants nothing there.</summary>
        public static AuthenticationBuilder AddClientToken(this AuthenticationBuilder builder) =>
            builder.AddScheme<AuthenticationSchemeOptions, ClientTokenAuthenticationHandler>(
                ClientTokenDefaults.AuthenticationScheme, null);
    }
}
