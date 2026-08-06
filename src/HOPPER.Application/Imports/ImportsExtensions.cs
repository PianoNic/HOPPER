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
                // A 340-file pack is a lot of individually small downloads, but a single 400 MB
                // content mod off a slow mirror is one long one - the default 100 seconds cuts those
                // off mid-file and turns them into pending entries for no reason.
                client.Timeout = TimeSpan.FromMinutes(10);

                // The same descriptive agent the Modrinth browser sends, not a generic "HOPPER/1.0".
                // This client already talks to api.modrinth.com - CurseForgeClient's blocked-file
                // fallback looks jars up by sha1 there - so a generic agent here is the one place that
                // could get a deployment's address flagged without anyone browsing anything.
                ModrinthExtensions.SetUserAgent(client);
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
