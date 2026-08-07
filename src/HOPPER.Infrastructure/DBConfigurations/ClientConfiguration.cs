using HOPPER.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HOPPER.Infrastructure.DBConfigurations
{
    public class ClientConfiguration : IEntityTypeConfiguration<Client>
    {
        public void Configure(EntityTypeBuilder<Client> builder)
        {
            builder.Property(c => c.ClientId).HasMaxLength(200);

            builder.Property(c => c.Username).HasMaxLength(100);

            builder.Property(c => c.LastIpAddress).HasMaxLength(45);

            builder.HasIndex(c => new { c.ServerId, c.ClientId }).IsUnique();
        }
    }
}
