namespace HOPPER.Domain.Enums
{
    /// <summary>Which mod loader a <see cref="Server"/> runs. Persisted as an int.
    ///
    /// One enum drives three separate external vocabularies: the Modrinth search facet
    /// ("categories:forge"), the .mrpack dependencies key ("forge"), the CurseForge manifest's
    /// modLoaders[].id prefix ("forge-") and the Prism component uid ("net.minecraftforge"). The
    /// lookup table that maps between them lives with the exporters, not here - the domain only
    /// records which loader it is.</summary>
    public enum ModLoader
    {
        /// <summary>Not configured. Means "the admin has not said", not "no loader" - a server
        /// created before the browser existed reads back as Unknown, and the browser and the pack
        /// export both refuse to run until it is set.</summary>
        Unknown = 0,

        Forge = 1,

        NeoForge = 2,

        Fabric = 3,

        Quilt = 4,
    }
}
