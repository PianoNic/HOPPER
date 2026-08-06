using HOPPER.API.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace HOPPER.API.Extensions
{
    public static class AuthExtensions
    {
        public const string DefaultAdminRole = "hopper-admin";

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
            options.TokenValidationParameters.RoleClaimType = "roles";

            options.TokenValidationParameters.ValidateAudience = configuration.GetValue("Oidc:ValidateAudience", true);
            options.TokenValidationParameters.ValidAudiences = ValidAudiences(configuration);
        }

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
