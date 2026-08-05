using HOPPER.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HOPPER.Infrastructure.DBConfigurations
{
    public class ModConfiguration : IEntityTypeConfiguration<Mod>
    {
        public void Configure(EntityTypeBuilder<Mod> builder)
        {
            // The client keys on filename, so two rows sharing one would make the manifest
            // self-contradictory: it would name the same target file twice with different hashes.
            builder.HasIndex(m => m.FileName).IsUnique();

            // Every blob request and the delete-orphan check look a mod up by hash.
            builder.HasIndex(m => m.Sha256);
        }
    }
}
