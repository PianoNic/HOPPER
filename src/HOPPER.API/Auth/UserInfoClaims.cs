using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using HOPPER.API.Extensions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Caching.Memory;

namespace HOPPER.API.Auth
{
    /// Roles the access token does not carry, fetched from the endpoint OIDC puts them on.
    ///
    /// Pocket ID publishes group membership in the ID token and userinfo and keeps the access token
    /// minimal; Okta and Entra leave groups out to bound token size. HOPPER validates the access
    /// token, so without this those deployments can never satisfy a role requirement.
    public static class UserInfoClaims
    {
        public const string HttpClientName = "hopper-userinfo";

        private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(5);

        public static Func<TokenValidatedContext, Task> Merge(IConfiguration configuration) =>
            async context =>
            {
                if (!configuration.GetValue("Oidc:FetchClaimsFromUserInfo", true))
                    return;

                var claim = AuthExtensions.RoleClaim(configuration);

                // Only when the token did not answer the question. An issuer that already puts roles
                // in the access token pays nothing for this.
                if (context.Principal?.Identity is not ClaimsIdentity identity || identity.HasClaim(c => c.Type == claim))
                    return;

                var token = ReadToken(context);
                if (token is null)
                    return;

                var services = context.HttpContext.RequestServices;
                var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("HOPPER.Auth");

                try
                {
                    var claims = await FetchAsync(services, context, token, logger, context.HttpContext.RequestAborted);

                    foreach (var (type, value) in claims.Where(c => !identity.HasClaim(c.Type, c.Value)))
                        identity.AddClaim(new Claim(type, value));
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
                {
                    // A userinfo endpoint that is down must not turn a valid login into a 500. The
                    // claims already on the token still decide.
                    logger.LogWarning(ex, "Could not read userinfo; deciding on the token's own claims.");
                }
            };

        private static async Task<IReadOnlyList<(string Type, string Value)>> FetchAsync(
            IServiceProvider services,
            TokenValidatedContext context,
            string token,
            ILogger logger,
            CancellationToken cancellationToken)
        {
            var cache = services.GetRequiredService<IMemoryCache>();
            var key = $"userinfo:{token.GetHashCode()}";

            if (cache.TryGetValue<IReadOnlyList<(string, string)>>(key, out var cached) && cached is not null)
                return cached;

            var endpoint = await EndpointAsync(context, cancellationToken);
            if (endpoint is null)
            {
                logger.LogWarning("The issuer publishes no userinfo endpoint, so roles must come from the token itself.");
                return [];
            }

            var http = services.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName);

            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await http.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("userinfo answered {Status}; deciding on the token's own claims.", (int)response.StatusCode);
                return [];
            }

            var claims = Parse(await response.Content.ReadAsStringAsync(cancellationToken));

            cache.Set(key, claims, CacheFor);
            return claims;
        }

        private static async Task<string?> EndpointAsync(TokenValidatedContext context, CancellationToken cancellationToken)
        {
            if (context.Options.ConfigurationManager is null)
                return context.Options.Configuration?.UserInfoEndpoint;

            var configuration = await context.Options.ConfigurationManager.GetConfigurationAsync(cancellationToken);
            return configuration.UserInfoEndpoint;
        }

        /// Scalars become one claim, arrays one per entry - which is how a groups array has to arrive
        /// for a role check to match any single group in it.
        public static IReadOnlyList<(string Type, string Value)> Parse(string json)
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return [];

            var claims = new List<(string, string)>();

            foreach (var property in document.RootElement.EnumerateObject())
            {
                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.String:
                        Add(property.Name, property.Value.GetString());
                        break;

                    case JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False:
                        Add(property.Name, property.Value.ToString());
                        break;

                    case JsonValueKind.Array:
                        foreach (var entry in property.Value.EnumerateArray())
                        {
                            Add(property.Name, entry.ValueKind == JsonValueKind.String
                                ? entry.GetString()
                                : entry.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? null : entry.ToString());
                        }

                        break;
                }
            }

            return claims;

            void Add(string type, string? value)
            {
                if (!string.IsNullOrEmpty(value))
                    claims.Add((type, value));
            }
        }

        private static string? ReadToken(TokenValidatedContext context)
        {
            var header = context.HttpContext.Request.Headers.Authorization.ToString();

            return header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? header["Bearer ".Length..].Trim()
                : null;
        }
    }
}
