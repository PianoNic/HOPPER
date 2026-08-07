using HOPPER.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HOPPER.Infrastructure.DBConfigurations
{
    public class ModConfiguration : IEntityTypeConfiguration<Mod>
    {
        public void Configure(EntityTypeBuilder<Mod> builder)
        {
            builder.Property(m => m.FileName).HasMaxLength(255);

            builder.Property(m => m.Sha256).HasMaxLength(64);

            builder.Property(m => m.Sha1).HasMaxLength(40);

            builder.Property(m => m.Sha512).HasMaxLength(128);

            builder.Property(m => m.UploadedBy).HasMaxLength(200);

            builder.Property(m => m.ProjectId).HasMaxLength(64);

            builder.Property(m => m.VersionId).HasMaxLength(64);

            builder.Property(m => m.ProjectName).HasMaxLength(255);

            builder.Property(m => m.DownloadUrl).HasMaxLength(2048);

            builder.HasIndex(m => m.Sha256);

            builder.HasIndex(m => new { m.ServerId, m.ProjectId });
        }
    }
}
