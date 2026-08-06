using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Application.Exports
{
    public static class ExportsExtensions
    {
        /// <summary>Three exporters behind one interface, resolved by format at the call site. Scoped,
        /// because each one holds the request's DbContext.</summary>
        public static IServiceCollection AddPackExports(this IServiceCollection services)
        {
            services.AddScoped<IPackExporter, MrpackExporter>();
            services.AddScoped<IPackExporter, CurseForgePackExporter>();
            services.AddScoped<IPackExporter, PrismInstanceExporter>();

            return services;
        }
    }
}
