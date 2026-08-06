using HOPPER.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HOPPER.Infrastructure.DBConfigurations
{
    public class ClientConfiguration : IEntityTypeConfiguration<Client>
    {
        public void Configure(EntityTypeBuilder<Client> builder)
        {
            // Reports upsert on this pair, so it has to be the natural key. A client id is a random
            // UUID minted per game directory, which makes it unique only by construction and only
            // within one server - scoping the index makes that explicit and stops one server's
            // client from colliding with another's.
            builder.HasIndex(c => new { c.ServerId, c.ClientId }).IsUnique();
        }
    }
}
