namespace HOPPER.Domain.Enums
{
    /// <summary>The archive shape the detector recognised. Persisted as an int.</summary>
    public enum PackFormat
    {
        /// <summary>Not yet detected, or detection failed before it could decide.</summary>
        Unknown = 0,

        /// <summary>A .mrpack: modrinth.index.json at the archive root plus overrides/.</summary>
        Modrinth = 1,

        /// <summary>A CurseForge pack zip: manifest.json at the archive root plus overrides/.</summary>
        CurseForge = 2,

        /// <summary>A Prism/MultiMC instance export, identified by an instance.cfg entry. It may
        /// itself wrap one of the two formats above, in which case detection re-runs on the
        /// stripped tree.</summary>
        PrismInstance = 3,

        /// <summary>A plain zip of jars - the multi-upload path, not a modpack.</summary>
        JarArchive = 4,
    }
}
