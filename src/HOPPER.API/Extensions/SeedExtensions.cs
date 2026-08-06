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
