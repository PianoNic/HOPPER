using HOPPER.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.API.Extensions
{
    public static class MigrationExtensions
    {
        public static void ApplyMigrations(this IApplicationBuilder app)
        {
            using var scope = app.ApplicationServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
            var logger = scope.ServiceProvider.GetRequiredService<ILogger<HopperDbContext>>();

            const int attempts = 10;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    db.Database.Migrate();
                    return;
                }
                catch (Exception ex) when (attempt < attempts)
                {
                    logger.LogWarning(ex, "Database not ready (attempt {Attempt}/{Attempts}), retrying", attempt, attempts);
                    Thread.Sleep(TimeSpan.FromSeconds(2));
                }
            }
        }
    }
}
