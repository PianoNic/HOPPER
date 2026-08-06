using HOPPER.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HOPPER.Infrastructure.DBConfigurations
{
    public class ModImportConfiguration : IEntityTypeConfiguration<ModImport>
    {
        public void Configure(EntityTypeBuilder<ModImport> builder)
        {
            builder.HasIndex(i => i.ServerId);
        }
    }
}
