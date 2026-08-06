using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Application.Imports
{
    public static class ImportsExtensions
    {
        public static IServiceCollection AddPackImports(this IServiceCollection services)
        {
            services.AddHttpClient(ImportHttpClients.Packs, client =>
            {
                // A 340-file pack is a lot of individually small downloads, but a single 400 MB
                // content mod off a slow mirror is one long one - the default 100 seconds cuts those
                // off mid-file and turns them into pending entries for no reason.
                client.Timeout = TimeSpan.FromMinutes(10);
                client.DefaultRequestHeaders.UserAgent.ParseAdd("HOPPER/1.0");
            });

            // Queue and staging are process-wide; the importer touches the database, so it is scoped
            // and the worker opens a scope per job.
            services.AddSingleton<IImportQueue, ImportQueue>();
            services.AddSingleton<IImportStaging, ImportStaging>();
            services.AddScoped<ICurseForgeClient, CurseForgeClient>();
            services.AddScoped<IPackImporter, PackImporter>();
            services.AddHostedService<ImportWorker>();

            return services;
        }
    }
}
