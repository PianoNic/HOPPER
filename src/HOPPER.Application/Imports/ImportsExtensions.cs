using HOPPER.Application.Modrinth;
using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Application.Imports
{
    public static class ImportsExtensions
    {
        public static IServiceCollection AddPackImports(this IServiceCollection services)
        {
            services.AddHttpClient(ImportHttpClients.Packs, client =>
                {
                    client.Timeout = TimeSpan.FromMinutes(10);

                    ModrinthExtensions.SetUserAgent(client);
                })
                .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });

            services.AddSingleton<ImportQueue>();
            services.AddSingleton<ImportStaging>();
            services.AddScoped<ICurseForgeClient, CurseForgeClient>();
            services.AddScoped<PackImporter>();
            services.AddHostedService<ImportWorker>();

            return services;
        }
    }
}
