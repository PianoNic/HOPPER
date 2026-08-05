using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Infrastructure.Extensions
{
    public static class BlobsExtensions
    {
        public static IServiceCollection AddBlobs(this IServiceCollection services)
        {
            // Stateless and config-only, so a singleton is right; nothing here touches the DbContext.
            services.AddSingleton<IBlobStorage, FileSystemBlobStorage>();
            return services;
        }
    }
}
