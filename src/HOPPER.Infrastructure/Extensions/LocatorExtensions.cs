using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Infrastructure.Extensions
{
    public static class LocatorExtensions
    {
        public static IServiceCollection AddLocatorJar(this IServiceCollection services)
        {
            services.AddSingleton<ILocatorJarBuilder, LocatorJarBuilder>();
            return services;
        }
    }
}
