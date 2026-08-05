using HOPPER.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.API.Extensions
{
    public static class MigrationExtensions
    {
        /// <summary>Brings the database up to the current model at boot, so deployment is just
        /// "start the new binary" and there is no separate `dotnet ef database update` step.
        ///
        /// <para>Retries, unlike the SQLite version this replaced: Postgres is a separate container
        /// and `depends_on` only waits for the process, not for it to accept connections. Without
        /// this, HOPPER crash-loops on a cold `docker compose up` until Postgres finishes its own
        /// first-run initialisation.</para></summary>
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
