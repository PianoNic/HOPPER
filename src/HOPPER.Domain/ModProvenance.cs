using HOPPER.Domain.Enums;

namespace HOPPER.Domain
{
    public static class ModProvenance
    {
        public static bool HasModrinthProvenance(this Mod mod) =>
            mod.Source == ModSource.Modrinth
            && !string.IsNullOrWhiteSpace(mod.ProjectId)
            && !string.IsNullOrWhiteSpace(mod.VersionId)
            && !string.IsNullOrWhiteSpace(mod.DownloadUrl)
            && !string.IsNullOrWhiteSpace(mod.Sha1)
            && !string.IsNullOrWhiteSpace(mod.Sha512);
    }
}
