using HOPPER.API.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace HOPPER.API.Extensions
{
    /// <summary>Who HOPPER trusts, and what being trusted gets you. Two questions, kept apart:
    /// authentication decides that a token is genuinely from the configured issuer and genuinely
    /// meant for this deployment, authorization decides that its holder administers HOPPER. Conflating
    /// them is how every account in a shared realm becomes an admin.
    ///
    /// Extracted from Program.cs so both halves can be asserted without a live IdP - see
    /// HOPPER.Tests/Api/AdminAuthorizationTests.cs.</summary>
    public static class AuthExtensions
    {
        /// <summary>Default role required on the admin surface. Overridable, and clearable, through
        /// Oidc:AdminRole.</summary>
        public const string DefaultAdminRole = "hopper-admin";

        public static IServiceCollection AddHopperAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options => ConfigureJwtBearer(options, configuration))
                .AddClientToken();

            return services;
        }

        /// <summary>Public so the tests can build the same TokenValidationParameters the running app
        /// gets, rather than a reconstruction of them that could drift.</summary>
        public static void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration)
        {
            // NullIfBlank, not ??. Compose interpolates an unset variable to an EMPTY STRING rather
            // than leaving the key absent, so `Oidc__InternalAuthority: "${Oidc__InternalAuthority:-}"`
            // arrives here as "" - which ?? happily accepts, skipping the block below and leaving the
            // scheme with neither a MetadataAddress nor an Authority. Every admin request then fails
            // to authenticate for a reason nothing in the configuration hints at.
            var publicAuthority = NullIfBlank(configuration["Oidc:Authority"]);
            var internalAuthority = NullIfBlank(configuration["Oidc:InternalAuthority"]) ?? publicAuthority;

            // In Docker the browser reaches the IdP on a published port while the API reaches it on
            // the compose network, so metadata is fetched from the internal URL but the issuer inside
            // the token is validated against the public one.
            if (internalAuthority is not null)
            {
                options.MetadataAddress = $"{internalAuthority.TrimEnd('/')}/.well-known/openid-configuration";
                options.TokenValidationParameters.ValidIssuer = publicAuthority;
            }

            options.RequireHttpsMetadata = configuration.GetValue("Oidc:RequireHttpsMetadata", true);

            // OFF, and the two lines below do not work without it.
            //
            // The default inbound map rewrites well-known short claim names to the old WS-* URIs on
            // the way in: "roles" becomes ".../ws/2008/06/identity/claims/role" and "name" becomes
            // ".../ws/2005/05/identity/claims/name". NameClaimType and RoleClaimType are matched
            // AFTER that rewrite, so asking for "roles" finds nothing - IsInRole is false for a token
            // that plainly carries the role, and every admin request 403s with the claim visible in
            // the token and invisible to the policy.
            options.MapInboundClaims = false;

            options.TokenValidationParameters.NameClaimType = "name";
            options.TokenValidationParameters.RoleClaimType = "roles";

            // ON, and the audiences default to this deployment's own client id.
            //
            // An issuer serves more than one application. With audience validation off, a token minted
            // for ANY client on the same realm - the Grafana next door, the Jellyfin someone set up
            // last year - validates here just as well as one minted for HOPPER, because the signature
            // and the issuer are all that get checked. Anyone able to obtain a token from that realm
            // for any purpose can then drive HOPPER's admin surface with it.
            //
            // Oidc:ValidAudiences overrides the default for an issuer that puts something else in aud;
            // Oidc:ValidateAudience=false is the explicit way out for one that emits nothing usable,
            // and is a decision worth writing down rather than a default worth inheriting.
            options.TokenValidationParameters.ValidateAudience = configuration.GetValue("Oidc:ValidateAudience", true);
            options.TokenValidationParameters.ValidAudiences = ValidAudiences(configuration);
        }

        public static IReadOnlyList<string> ValidAudiences(IConfiguration configuration)
        {
            var configured = configuration.GetSection("Oidc:ValidAudiences").Get<string[]>();
            if (configured is { Length: > 0 })
                return configured;

            // The client id is the audience a correctly configured issuer stamps into a token minted
            // for HOPPER, so it is the right default and needs no second setting to state twice.
            return NullIfBlank(configuration["Oidc:ClientId"]) is { } clientId ? [clientId] : [];
        }

        public static IServiceCollection AddHopperAuthorization(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAuthorization(options => options.FallbackPolicy = BuildAdminPolicy(configuration));
            return services;
        }

        /// <summary>The fallback policy every endpoint gets unless it says otherwise.
        ///
        /// Secure by default: an endpoint is protected by doing nothing. Client endpoints carry an
        /// explicit [Authorize(AuthenticationSchemes = "ClientToken")], which replaces this policy for
        /// those actions, so an OIDC token never opens the manifest and a client token never opens the
        /// admin surface.</summary>
        public static AuthorizationPolicy BuildAdminPolicy(IConfiguration configuration)
        {
            var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser();

            // Authentication is not authorization. Without this, every account the issuer knows about
            // is a full HOPPER admin - and .env.example invites operators to point Oidc:Authority at
            // the realm they already run, where the other accounts belong to people who were never
            // given HOPPER at all. Requiring a role keeps "has an account here" apart from
            // "administers HOPPER".
            //
            // Set Oidc:AdminRole to "" to go back to any authenticated user. That is a real
            // configuration for a realm HOPPER is the only client of, and it stays available - it is
            // just no longer what an operator gets by not thinking about it.
            var adminRole = configuration.GetValue("Oidc:AdminRole", DefaultAdminRole);
            if (!string.IsNullOrWhiteSpace(adminRole))
                policy = policy.RequireRole(adminRole);

            return policy.Build();
        }

        /// <summary>Treats an empty or whitespace configuration value as absent. Needed wherever a
        /// value can arrive from Compose interpolation, which writes "" for an unset variable instead
        /// of omitting the key.</summary>
        private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
