using System.Text.Json.Serialization;

namespace HOPPER.Application.Exports.Schema
{
    /// <summary>manifest.json of a CurseForge pack zip.
    ///
    /// An external FILE FORMAT contract - do not rename these. Note especially projectID and fileID
    /// on the file entry: the capitalisation is theirs, it is not a typo, and a camelCase serializer
    /// policy would silently produce a manifest no launcher reads.</summary>
    public sealed record CurseForgeManifest
    {
        [JsonPropertyName("minecraft")]
        public required CurseForgeMinecraft Minecraft { get; init; }

        /// <summary>Must be exactly "minecraftModpack". Consumers require it.</summary>
        [JsonPropertyName("manifestType")]
        public string ManifestType { get; init; } = "minecraftModpack";

        [JsonPropertyName("manifestVersion")]
        public int ManifestVersion { get; init; } = 1;

        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("version")]
        public required string Version { get; init; }

        [JsonPropertyName("author")]
        public required string Author { get; init; }

        /// <summary>The overrides folder's name. It is a FIELD and not a constant - consumers read it
        /// rather than assuming "overrides", and HOPPER's own importer already does the same.</summary>
        [JsonPropertyName("overrides")]
        public string Overrides { get; init; } = "overrides";

        /// <summary>Addressed by numeric CurseForge project and file ids. A Modrinth-sourced mod has
        /// neither and they cannot be invented, so this list is empty unless a mod carries real
        /// CurseForge provenance - every other jar ships inline in overrides/mods/. An empty files[]
        /// is a valid, importable pack, not a compromise.</summary>
        [JsonPropertyName("files")]
        public required IReadOnlyList<CurseForgeFileEntry> Files { get; init; }
    }

    public sealed record CurseForgeMinecraft
    {
        [JsonPropertyName("version")]
        public required string Version { get; init; }

        [JsonPropertyName("modLoaders")]
        public required IReadOnlyList<CurseForgeModLoader> ModLoaders { get; init; }
    }

    public sealed record CurseForgeModLoader
    {
        /// <summary>"&lt;loader&gt;-&lt;version&gt;", with the bare loader build and no Minecraft
        /// prefix.</summary>
        [JsonPropertyName("id")]
        public required string Id { get; init; }

        [JsonPropertyName("primary")]
        public bool Primary { get; init; } = true;
    }

    public sealed record CurseForgeFileEntry
    {
        [JsonPropertyName("projectID")]
        public required int ProjectId { get; init; }

        [JsonPropertyName("fileID")]
        public required int FileId { get; init; }

        [JsonPropertyName("required")]
        public bool Required { get; init; } = true;
    }
}
