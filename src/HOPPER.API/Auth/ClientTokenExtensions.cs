using Microsoft.AspNetCore.Authentication;

namespace HOPPER.API.Auth
{
    public static class ClientTokenExtensions
    {
        public static AuthenticationBuilder AddClientToken(this AuthenticationBuilder builder) =>
            builder.AddScheme<AuthenticationSchemeOptions, ClientTokenAuthenticationHandler>(
                ClientTokenDefaults.AuthenticationScheme, null);
    }
}
