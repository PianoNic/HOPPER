using HOPPER.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HOPPER.Infrastructure.DBConfigurations
{
    public class ModImportConfiguration : IEntityTypeConfiguration<ModImport>
    {
        public void Configure(EntityTypeBuilder<ModImport> builder)
        {
            // The history table reads one server's imports, newest first.
            builder.HasIndex(i => i.ServerId);
        }
    }
}
