using HOPPER.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HOPPER.Infrastructure.DBConfigurations
{
    public class PendingModConfiguration : IEntityTypeConfiguration<PendingMod>
    {
        public void Configure(EntityTypeBuilder<PendingMod> builder)
        {
            // The pending list is per server and outlives the import that produced it.
            builder.HasIndex(p => p.ServerId);

            // Deleting a server, and showing one import's own pendings, both filter on the import.
            builder.HasIndex(p => p.ImportId);
        }
    }
}
