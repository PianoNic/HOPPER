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
            });

            services.AddSingleton<IImportQueue, ImportQueue>();
            services.AddSingleton<IImportStaging, ImportStaging>();
            services.AddScoped<ICurseForgeClient, CurseForgeClient>();
            services.AddScoped<IPackImporter, PackImporter>();
            services.AddHostedService<ImportWorker>();

            return services;
        }
    }
}
