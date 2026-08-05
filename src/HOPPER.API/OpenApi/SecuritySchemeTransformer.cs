using HOPPER.API.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace HOPPER.API.OpenApi
{
    /// <summary>Declares both authentication schemes in the OpenAPI document and applies them per
    /// operation. Unlike KRINT this cannot blanket-apply one scheme, because HOPPER has two audiences
    /// on the same document: the dashboard (OAuth2) and the game client (a shared bearer token).
    /// Telling Scalar that /api/manifest takes an OAuth2 token would make it impossible to try the
    /// endpoint the way the real client calls it.</summary>
    internal sealed class SecuritySchemeTransformer(
        IAuthenticationSchemeProvider authenticationSchemeProvider,
        IConfiguration configuration) : IOpenApiDocumentTransformer
    {
        private const string OAuth2SchemeName = "OAuth2";

        /// <summary>Routes served to the Forge locator. Everything else in the document is admin
        /// surface, except /api/app which is anonymous.</summary>
        private static readonly string[] ClientTokenPaths =
        [
            "/api/manifest",
            "/api/blobs",
            "/api/clients/report",
        ];

        private const string AnonymousPath = "/api/app";

        public async Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
        {
            var schemes = await authenticationSchemeProvider.GetAllSchemesAsync();

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

            document.Components.SecuritySchemes[ClientTokenDefaults.AuthenticationScheme] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                Description = "The shared token from Hopper:ClientTokens, as configured in the player's hopper.properties.",
            };

            // The OAuth2 scheme needs a live authority to describe its endpoints. A dev instance
            // running without an IdP still has to produce a usable document — the frontend's client
            // generator reads it — so its absence degrades the document rather than failing it.
            var authority = configuration["Oidc:Authority"]?.TrimEnd('/');
            var hasOAuth2 = schemes.Any(scheme => scheme.Name == JwtBearerDefaults.AuthenticationScheme)
                && !string.IsNullOrWhiteSpace(authority);

            if (hasOAuth2)
            {
                document.Components.SecuritySchemes[OAuth2SchemeName] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.OAuth2,
                    Flows = new OpenApiOAuthFlows
                    {
                        AuthorizationCode = new OpenApiOAuthFlow
                        {
                            AuthorizationUrl = new Uri($"{authority}/protocol/openid-connect/auth"),
                            TokenUrl = new Uri($"{authority}/protocol/openid-connect/token"),
                            Scopes = new Dictionary<string, string>
                            {
                                ["openid"] = "OpenID",
                                ["profile"] = "Profile",
                                ["email"] = "Email",
                                ["roles"] = "Roles",
                            },
                        },
                    },
                };
            }

            foreach (var (path, item) in document.Paths)
            {
                if (item.Operations is null)
                    continue;

                if (path.StartsWith(AnonymousPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                var usesClientToken = ClientTokenPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));

                // /api/clients is admin but /api/clients/report is not, and the prefix match above
                // already separated them.
                if (!usesClientToken && !hasOAuth2)
                    continue;

                foreach (var operation in item.Operations)
                {
                    operation.Value.Security ??= new List<OpenApiSecurityRequirement>();

                    if (usesClientToken)
                    {
                        operation.Value.Security.Add(new OpenApiSecurityRequirement
                        {
                            [new OpenApiSecuritySchemeReference(ClientTokenDefaults.AuthenticationScheme, document)] = new List<string>(),
                        });
                    }
                    else
                    {
                        operation.Value.Security.Add(new OpenApiSecurityRequirement
                        {
                            [new OpenApiSecuritySchemeReference(OAuth2SchemeName, document)] = new List<string> { "openid", "profile", "email", "roles" },
                        });
                    }
                }
            }
        }
    }
}
