using HOPPER.Domain;
using HOPPER.Infrastructure.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HOPPER.Infrastructure
{
    public class HopperDbContext(DbContextOptions<HopperDbContext> options) : DbContext(options)
    {
        public DbSet<Server> Servers => Set<Server>();
        public DbSet<Mod> Mods => Set<Mod>();
        public DbSet<Client> Clients => Set<Client>();
        public DbSet<ClientReportedMod> ClientReportedMods => Set<ClientReportedMod>();
        public DbSet<ModImport> ModImports => Set<ModImport>();
        public DbSet<PendingMod> PendingMods => Set<PendingMod>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(HopperDbContext).Assembly);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            ApplySaveChangesGuards();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            ApplySaveChangesGuards();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        private void ApplySaveChangesGuards()
        {
            foreach (var entry in ChangeTracker.Entries<BaseEntity>())
            {
                if (entry.State == EntityState.Modified)
                    entry.Entity.UpdatedAt = DateTime.UtcNow;
            }
        }
    }

    public class HopperDbContextFactory : IDesignTimeDbContextFactory<HopperDbContext>
    {
        public HopperDbContext CreateDbContext(string[] args)
        {
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__HopperDatabase");
            var optionsBuilder = new DbContextOptionsBuilder<HopperDbContext>();
            optionsBuilder.ConfigureHopperProvider(connectionString);
            return new HopperDbContext(optionsBuilder.Options);
        }
    }
}
