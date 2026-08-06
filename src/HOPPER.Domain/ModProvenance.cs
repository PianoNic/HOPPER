using HOPPER.Domain.Enums;

namespace HOPPER.Domain
{
    /// <summary>The one place that decides whether a mod's provenance is good enough to be used.
    /// Every caller goes through here rather than testing <see cref="Mod.Source"/> itself.</summary>
    public static class ModProvenance
    {
        /// <summary>True only when every field a .mrpack files[] entry needs is actually present.
        ///
        /// The exporters test this, not Source, and the difference matters: a row whose Source says
        /// Modrinth but whose DownloadUrl or hashes are missing would otherwise produce a manifest
        /// entry with a null URL, which is an unusable pack. Failing this check is not an error - it
        /// means the jar is written into overrides/ as bytes instead, which is a correct pack either
        /// way.</summary>
        public static bool HasModrinthProvenance(this Mod mod) =>
            mod.Source == ModSource.Modrinth
            && !string.IsNullOrWhiteSpace(mod.ProjectId)
            && !string.IsNullOrWhiteSpace(mod.VersionId)
            && !string.IsNullOrWhiteSpace(mod.DownloadUrl)
            && !string.IsNullOrWhiteSpace(mod.Sha1)
            && !string.IsNullOrWhiteSpace(mod.Sha512);
    }
}
