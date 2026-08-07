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
                // The same User-Agent the Modrinth client sends: these are other people's servers
                // and they are entitled to know who is calling.
                ModrinthExtensions.SetUserAgent(client);

                // Short, unlike the Modrinth client's ten minutes. Nothing here is a download, and
                // a dialog waiting on a version list has to give up quickly and let it be typed.
                client.Timeout = TimeSpan.FromSeconds(15);
            });

            services.AddMemoryCache();
            services.AddScoped<ILoaderVersionClient, LoaderVersionClient>();

            return services;
        }
    }
}
