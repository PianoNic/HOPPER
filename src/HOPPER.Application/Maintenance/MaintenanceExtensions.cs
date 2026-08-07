using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Application.Maintenance
{
    public static class MaintenanceExtensions
    {
        public static IServiceCollection AddBlobReclaim(this IServiceCollection services)
        {
            services.AddScoped<BlobReclaimer>();
            services.AddHostedService<BlobReclaimService>();

            return services;
        }
    }
}
