using HOPPER.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HOPPER.Infrastructure.DBConfigurations
{
    public class ModConfiguration : IEntityTypeConfiguration<Mod>
    {
        public void Configure(EntityTypeBuilder<Mod> builder)
        {
            // The client keys on filename, so two rows sharing one would make that server's manifest
            // self-contradictory: it would name the same target file twice with different hashes.
            // Scoped to the server, not global - two servers running the same modpack must both be
            // able to carry jei.jar, and they are different manifests.
            // It also serves every "this server's mods" read - listing and manifest generation both
            // filter on ServerId alone, and ServerId is this index's leftmost column, so a separate
            // single-column index would be dead weight to maintain on every write.
            builder.HasIndex(m => new { m.ServerId, m.FileName }).IsUnique();

            // Every blob request and the delete-orphan check look a mod up by hash. Deliberately not
            // composite with ServerId and deliberately not unique: the orphan check has to ask "does
            // ANY server still reference this blob", which is a lookup on the hash alone.
            builder.HasIndex(m => m.Sha256);
        }
    }
}
