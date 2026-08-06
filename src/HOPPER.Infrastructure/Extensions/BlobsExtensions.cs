using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Infrastructure.Extensions
{
    public static class BlobsExtensions
    {
        public static IServiceCollection AddBlobs(this IServiceCollection services)
        {
            services.AddSingleton<IBlobStorage, FileSystemBlobStorage>();
            return services;
        }
    }
}
