using HOPPER.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HOPPER.Infrastructure.DBConfigurations
{
    public class ClientReportedModConfiguration : IEntityTypeConfiguration<ClientReportedMod>
    {
        public void Configure(EntityTypeBuilder<ClientReportedMod> builder)
        {
            builder.HasIndex(r => r.ClientId);
        }
    }
}
