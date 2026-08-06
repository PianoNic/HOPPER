namespace HOPPER.Domain.Enums
{
    /// <summary>Where a <see cref="Mod"/>'s jar came from. Persisted as an int.
    ///
    /// Manual is 0 so that every row written before provenance existed reads back as Manual without
    /// a data migration. This value alone is never enough to trust the provenance columns - use
    /// <see cref="ModProvenance.HasModrinthProvenance"/>, which checks that every field a pack
    /// manifest entry needs is actually populated.</summary>
    public enum ModSource
    {
        /// <summary>Hand-uploaded, or imported from a pack before provenance was recorded. Every
        /// provenance column is null and the exporters ship the jar as a file in overrides/.</summary>
        Manual = 0,

        /// <summary>Added through the Modrinth browser. Carries project id, version id, the CDN
        /// download URL and Modrinth's published sha1/sha512.</summary>
        Modrinth = 1,

        /// <summary>Resolved against CurseForge. Nothing writes this yet - the value exists from the
        /// start so the pack importer can begin recording CurseForge provenance later without a
        /// second migration, and so the exporter's files[] branch has something to key on.</summary>
        CurseForge = 2,
    }
}
