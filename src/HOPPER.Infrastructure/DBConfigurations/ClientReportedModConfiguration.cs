using HOPPER.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HOPPER.Infrastructure.DBConfigurations
{
    public class ClientReportedModConfiguration : IEntityTypeConfiguration<ClientReportedMod>
    {
        public void Configure(EntityTypeBuilder<ClientReportedMod> builder)
        {
            builder.Property(r => r.FileName).HasMaxLength(255);

            builder.Property(r => r.Sha256).HasMaxLength(64);

            builder.HasIndex(r => r.ClientId);
        }
    }
}
