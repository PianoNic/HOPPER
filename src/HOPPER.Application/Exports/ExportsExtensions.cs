using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Application.Exports
{
    public static class ExportsExtensions
    {
        public static IServiceCollection AddPackExports(this IServiceCollection services)
        {
            services.AddScoped<IPackExporter, MrpackExporter>();
            services.AddScoped<IPackExporter, CurseForgePackExporter>();
            services.AddScoped<IPackExporter, PrismInstanceExporter>();

            return services;
        }
    }
}
