using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Infrastructure.Extensions
{
    public static class DatabaseExtensions
    {
        private const string DefaultConnectionString =
            "Host=localhost;Port=5433;Database=hopper;Username=hopper;Password=hopper";

        public static IServiceCollection AddHopperDatabase(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("HopperDatabase");
            services.AddDbContext<HopperDbContext>(options => options.ConfigureHopperProvider(connectionString));
            return services;
        }

        public static DbContextOptionsBuilder ConfigureHopperProvider(
            this DbContextOptionsBuilder options, string? connectionString)
        {
            options.UseNpgsql(string.IsNullOrWhiteSpace(connectionString)
                ? DefaultConnectionString
                : connectionString);
            return options;
        }
    }
}
