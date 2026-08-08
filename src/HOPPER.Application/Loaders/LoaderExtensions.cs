using HOPPER.Application.Modrinth;
using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Application.Loaders
{
    public static class LoaderExtensions
    {
        public static IServiceCollection AddLoaderVersions(this IServiceCollection services)
        {
            services.AddHttpClient(LoaderVersionClient.HttpClientName, client =>
            {
                ModrinthExtensions.SetUserAgent(client);

                client.Timeout = TimeSpan.FromSeconds(15);
            });

            services.AddMemoryCache();
            services.AddScoped<LoaderVersionClient>();

            return services;
        }
    }
}
