using HOPPER.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HOPPER.Infrastructure.DBConfigurations
{
    public class ClientReportedModConfiguration : IEntityTypeConfiguration<ClientReportedMod>
    {
        public void Configure(EntityTypeBuilder<ClientReportedMod> builder)
        {
            // Every report deletes this client's existing rows before inserting the new set, and
            // the dashboard groups by client — both are lookups on this column alone.
            builder.HasIndex(r => r.ClientId);
        }
    }
}
