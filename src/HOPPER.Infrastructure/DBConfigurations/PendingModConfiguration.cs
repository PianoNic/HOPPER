using HOPPER.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HOPPER.Infrastructure.DBConfigurations
{
    public class PendingModConfiguration : IEntityTypeConfiguration<PendingMod>
    {
        public void Configure(EntityTypeBuilder<PendingMod> builder)
        {
            builder.HasIndex(p => p.ServerId);

            builder.HasIndex(p => p.ImportId);
        }
    }
}
