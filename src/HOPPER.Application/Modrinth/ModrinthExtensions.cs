using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Application.Modrinth
{
    public static class ModrinthExtensions
    {
        public static string UserAgent => $"PianoNic/HOPPER/{HopperVersion.Current} (github.com/PianoNic/HOPPER)";

        public static void SetUserAgent(HttpClient client)
        {
            client.DefaultRequestHeaders.Remove("User-Agent");
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        }

        public static IServiceCollection AddModrinth(this IServiceCollection services)
        {
            services.AddSingleton<ModrinthRateLimiter>();
            services.AddTransient<ModrinthRateLimitHandler>();

            services.AddHttpClient(ModrinthHttpClients.Modrinth, client =>
                {
                    client.BaseAddress = new Uri("https://api.modrinth.com/v2/");
                    SetUserAgent(client);

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
