using HOPPER.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HOPPER.Infrastructure.DBConfigurations
{
    public class ServerConfiguration : IEntityTypeConfiguration<Server>
    {
        public void Configure(EntityTypeBuilder<Server> builder)
        {
            builder.Property(s => s.Name).HasMaxLength(200);

            builder.Property(s => s.Slug).HasMaxLength(100);

            builder.Property(s => s.Token).HasMaxLength(200);

            builder.HasIndex(s => s.Slug).IsUnique();

            builder.HasIndex(s => s.Token).IsUnique();
        }
    }
}
