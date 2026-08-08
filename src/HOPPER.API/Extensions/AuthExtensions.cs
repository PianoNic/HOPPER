using HOPPER.API.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace HOPPER.API.Extensions
{
    public static class AuthExtensions
    {
        public const string DefaultAdminRole = "hopper-admin";

        public const string DefaultRoleClaim = "roles";

        public static IServiceCollection AddHopperAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options => ConfigureJwtBearer(options, configuration))
                .AddClientToken();

            return services;
        }

        public static void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration)
        {
            var publicAuthority = NullIfBlank(configuration["Oidc:Authority"]);
            var internalAuthority = NullIfBlank(configuration["Oidc:InternalAuthority"]) ?? publicAuthority;

            if (internalAuthority is not null)
            {
                options.MetadataAddress = $"{internalAuthority.TrimEnd('/')}/.well-known/openid-configuration";
                options.TokenValidationParameters.ValidIssuer = publicAuthority;
            }

            options.RequireHttpsMetadata = configuration.GetValue("Oidc:RequireHttpsMetadata", true);

            options.MapInboundClaims = false;

            options.TokenValidationParameters.NameClaimType = "name";
            // Configurable because issuers disagree: Pocket ID, Authentik and Keycloak publish
            // membership as `groups`, and looking in the wrong claim 403s every admin request while
            // the token itself is perfectly valid.
            options.TokenValidationParameters.RoleClaimType = RoleClaim(configuration);

            options.TokenValidationParameters.ValidateAudience = configuration.GetValue("Oidc:ValidateAudience", true);
            options.TokenValidationParameters.ValidAudiences = ValidAudiences(configuration);

            options.Events = new JwtBearerEvents { OnForbidden = ExplainForbidden(configuration) };
        }

        /// A 403 here means the token was accepted and the role was not found, which is invisible
        /// from the outside: an empty body, and a valid login. Say which claim was read and what the
        /// token actually carried, because that pair is the whole answer.
        private static Func<ForbiddenContext, Task> ExplainForbidden(IConfiguration configuration) =>
            context =>
            {
                var claim = RoleClaim(configuration);
                var wanted = configuration.GetValue("Oidc:AdminRole", DefaultAdminRole);

                // HttpContext.User, not context.Principal: the handler builds ForbiddenContext
                // without a principal, so reading it reports a token with no claims at all and
                // sends whoever is debugging looking for the wrong problem.
                var user = context.HttpContext.User;

                var carried = user.Claims
                    .Where(c => string.Equals(c.Type, claim, StringComparison.Ordinal))
                    .Select(c => c.Value)
                    .ToList();

                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("HOPPER.Auth");

                logger.LogWarning(
                    "{Path} refused: the token is valid but carries no {Role} in its '{Claim}' claim. "
                    + "It carries [{Carried}] there, and these claim types: [{Types}]. "
                    + "Set Oidc:RoleClaim if your issuer publishes membership somewhere else.",
                    context.HttpContext.Request.Path,
                    wanted,
                    claim,
                    string.Join(", ", carried),
                    string.Join(", ", user.Claims.Select(c => c.Type).Distinct()));

                return Task.CompletedTask;
            };

        public static IReadOnlyList<string> ValidAudiences(IConfiguration configuration)
        {
            var configured = configuration.GetSection("Oidc:ValidAudiences").Get<string[]>();
            if (configured is { Length: > 0 })
                return configured;

            return NullIfBlank(configuration["Oidc:ClientId"]) is { } clientId ? [clientId] : [];
        }

        public static IServiceCollection AddHopperAuthorization(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAuthorization(options => options.FallbackPolicy = BuildAdminPolicy(configuration));
            return services;
        }

        public static string RoleClaim(IConfiguration configuration) =>
            NullIfBlank(configuration["Oidc:RoleClaim"]) ?? DefaultRoleClaim;

        public static AuthorizationPolicy BuildAdminPolicy(IConfiguration configuration)
        {
            var policy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser();

            var adminRole = configuration.GetValue("Oidc:AdminRole", DefaultAdminRole);
            if (!string.IsNullOrWhiteSpace(adminRole))
                policy = policy.RequireRole(adminRole);

            return policy.Build();
        }

        private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
