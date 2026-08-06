using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Application.Modrinth
{
    public static class ModrinthExtensions
    {
        /// <summary>Modrinth require a uniquely identifying User-Agent and document that a generic one
        /// raises the likelihood of being blocked. This is their documented best tier: GitHub user,
        /// project, version, and contact information.
        ///
        /// Worth knowing so nobody tests it away: a missing or generic agent is NOT rejected at
        /// request time. The blocking is reputation-based and applied later, so a passing request
        /// proves nothing about whether the agent is acceptable. The version comes from
        /// application.properties by way of Directory.Build.props, so a release bumps it on its own
        /// and a blocked deployment can be correlated with a build.</summary>
        public static string UserAgent => $"PianoNic/HOPPER/{HopperVersion.Current} (github.com/PianoNic/HOPPER)";

        /// <summary>Sets the agent WITHOUT .NET's header validation, and that is not laziness.
        ///
        /// Modrinth's documented best-practice agent is "github_user/project/version (contact)", but
        /// RFC 7230 says a product token is one "name/version" with a SINGLE slash, so
        /// "PianoNic/HOPPER/1.0.0" is two slashes and HttpHeaders.ParseAdd throws FormatException on
        /// it. Validating would mean either an agent Modrinth ask us not to send or an exception at
        /// the moment the client is created - which takes down the pack importer too, since it shares
        /// this string. The value is a compile-time constant plus a version read from the assembly, so
        /// there is nothing here for an attacker to inject.</summary>
        public static void SetUserAgent(HttpClient client)
        {
            client.DefaultRequestHeaders.Remove("User-Agent");
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        }

        public static IServiceCollection AddModrinth(this IServiceCollection services)
        {
            // Process-wide, because the limit Modrinth enforce is per IP and this process has one.
            services.AddSingleton<ModrinthRateLimiter>();
            services.AddTransient<ModrinthRateLimitHandler>();

            services.AddHttpClient(ModrinthHttpClients.Modrinth, client =>
                {
                    // Trailing slash, so relative request URIs append rather than replace the path.
                    client.BaseAddress = new Uri("https://api.modrinth.com/v2/");
                    SetUserAgent(client);

                    // The same client streams jar downloads off the CDN, and a 400 MB content mod on a
                    // slow link outlives the default 100 seconds.
                    client.Timeout = TimeSpan.FromMinutes(10);
                })
                .AddHttpMessageHandler<ModrinthRateLimitHandler>();

            services.AddMemoryCache();
            services.AddScoped<IModrinthClient, ModrinthClient>();
            services.AddScoped<IModrinthDependencyResolver, ModrinthDependencyResolver>();

            return services;
        }
    }
}
