using HOPPER.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HOPPER.Infrastructure.DBConfigurations
{
    public class ModConfiguration : IEntityTypeConfiguration<Mod>
    {
        public void Configure(EntityTypeBuilder<Mod> builder)
        {
            builder.HasIndex(m => new { m.ServerId, m.FileName }).IsUnique();

            builder.HasIndex(m => m.Sha256);

            builder.HasIndex(m => new { m.ServerId, m.ProjectId });
        }
    }
}
