using HOPPER.Application;
using HOPPER.Domain;
using HOPPER.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.API.Extensions
{
    public static class SeedExtensions
    {
        public static async Task<WebApplication> ApplySeedsAsync(this WebApplication app)
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();

            await SeedDefaultServerAsync(db, app.Configuration);

            return app;
        }

        /// <summary>Creates one "Default" server on an empty database, so `docker compose up` is a
        /// working demo rather than an install with no valid token in existence.
        ///
        /// Guarded on the table being empty, not on the token value, so it can never overwrite a
        /// token the admin has rotated. Hopper:BootstrapClientToken lets a deployment pin the first
        /// token to something it already put in a compose file; without it a random one is minted and
        /// the admin reads it off the setup page. The token is never logged either way.</summary>
        private static async Task SeedDefaultServerAsync(HopperDbContext db, IConfiguration configuration)
        {
            if (await db.Servers.AnyAsync())
                return;

            var configured = configuration["Hopper:BootstrapClientToken"];

            db.Servers.Add(new Server
            {
                Name = "Default",
                Slug = "default",
                Token = string.IsNullOrWhiteSpace(configured) ? ServerTokenGenerator.New() : configured,
            });

            await db.SaveChangesAsync();
        }
    }
}
