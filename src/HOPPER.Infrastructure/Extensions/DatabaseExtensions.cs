using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Infrastructure.Extensions
{
    public static class DatabaseExtensions
    {
        /// <summary>
        /// Matches the postgres service in compose.dev.yml, so a fresh checkout runs against
        /// `docker compose -f compose.dev.yml up -d` without configuring anything. These are local
        /// container credentials, not a secret. Deployments set ConnectionStrings__HopperDatabase
        /// explicitly (see compose.yml / .env.example).
        /// </summary>
        private const string DefaultConnectionString =
            "Host=localhost;Port=5433;Database=hopper;Username=hopper;Password=hopper";

        public static IServiceCollection AddHopperDatabase(this IServiceCollection services, IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString("HopperDatabase");
            services.AddDbContext<HopperDbContext>(options => options.ConfigureHopperProvider(connectionString));
            return services;
        }

        // No MigrationsAssembly here on purpose: HOPPER is Postgres-only, so migrations live in the
        // DbContext's own assembly. Pointing this at a separate project that does not exist would
        // make Migrate() silently find zero migrations and start against an empty database.
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
