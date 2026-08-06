using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Infrastructure.Extensions
{
    public static class LocatorExtensions
    {
        public static IServiceCollection AddLocatorJar(this IServiceCollection services)
        {
            // Config-only and holds no per-request state, so a singleton is right. Note it does not
            // cache the template's bytes: an admin who rebuilds the jar and restarts nothing should
            // still get the new one.
            services.AddSingleton<ILocatorJarBuilder, LocatorJarBuilder>();
            return services;
        }
    }
}
