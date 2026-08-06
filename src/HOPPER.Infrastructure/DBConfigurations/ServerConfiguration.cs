using HOPPER.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HOPPER.Infrastructure.DBConfigurations
{
    public class ServerConfiguration : IEntityTypeConfiguration<Server>
    {
        public void Configure(EntityTypeBuilder<Server> builder)
        {
            // The slug is what names the generated jar and identifies the server to a human, so two
            // servers sharing one would produce two different <slug>-hopper.jar files.
            builder.HasIndex(s => s.Slug).IsUnique();

            // Every client request resolves a bearer token to exactly one server. A duplicate token
            // would make that resolution ambiguous, which is a tenant-isolation failure rather than
            // a mere data-quality one, so the database refuses it outright.
            builder.HasIndex(s => s.Token).IsUnique();
        }
    }
}
