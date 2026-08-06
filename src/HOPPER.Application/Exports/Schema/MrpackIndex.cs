using System.Text.Json.Serialization;

namespace HOPPER.Application.Exports.Schema
{
    /// <summary>modrinth.index.json, format version 1.
    ///
    /// These names are an external FILE FORMAT contract, exactly like the manifest DTOs are a wire
    /// contract. Every one of them is read by Modrinth's own tooling and by Prism, so do not rename
    /// them, do not let a serializer policy case them differently, and do not add fields that are not
    /// in the format.</summary>
    public sealed record MrpackIndex
    {
        [JsonPropertyName("formatVersion")]
        public int FormatVersion { get; init; } = 1;

        /// <summary>Only "minecraft" is defined.</summary>
        [JsonPropertyName("game")]
        public string Game { get; init; } = "minecraft";

        [JsonPropertyName("versionId")]
        public required string VersionId { get; init; }

        [JsonPropertyName("name")]
        public required string Name { get; init; }

        [JsonPropertyName("summary")]
        public string? Summary { get; init; }

        /// <summary>May be empty, and legitimately is when every mod on the server was hand-uploaded:
        /// the jars are then all in overrides/ and the pack is still valid and importable.</summary>
        [JsonPropertyName("files")]
        public required IReadOnlyList<MrpackFile> Files { get; init; }

        /// <summary>Keyed by loader id. Export emits only "minecraft" plus one loader key from
        /// LoaderIds, never an arbitrary key.</summary>
        [JsonPropertyName("dependencies")]
        public required IReadOnlyDictionary<string, string> Dependencies { get; init; }
    }

    public sealed record MrpackFile
    {
        /// <summary>Relative to the .minecraft directory, forward slashes. HOPPER always writes a flat
        /// "mods/&lt;filename&gt;" - consumers reject a path containing "..", a drive letter or a
        /// leading separator, and the filename is already validated against exactly those.</summary>
        [JsonPropertyName("path")]
        public required string Path { get; init; }

        /// <summary>Must carry BOTH sha1 and sha512. Note what is absent: sha256. It is not an
        /// algorithm this format or any of its consumers know, and it stays where it belongs, as the
        /// blob address.</summary>
        [JsonPropertyName("hashes")]
        public required IReadOnlyDictionary<string, string> Hashes { get; init; }

        [JsonPropertyName("env")]
        public MrpackEnv? Env { get; init; }

        /// <summary>Mirrors of the same file. HOPPER writes the one upstream CDN URL it recorded as
        /// provenance and never its own blob URL - a HOPPER URL needs this server's token, so a pack
        /// carrying one would be unusable to anyone it was handed to and unuploadable to Modrinth,
        /// whose whitelist is cdn.modrinth.com, github.com, raw.githubusercontent.com and
        /// gitlab.com.</summary>
        [JsonPropertyName("downloads")]
        public required IReadOnlyList<string> Downloads { get; init; }

        [JsonPropertyName("fileSize")]
        public required long FileSize { get; init; }
    }

    /// <summary>required | optional | unsupported on each side. HOPPER has no per-mod side
    /// information - a server's mod list is one list - so export always writes required/required.
    /// Import is the tolerant direction and already accepts values outside the spec.</summary>
    public sealed record MrpackEnv
    {
        [JsonPropertyName("client")]
        public string Client { get; init; } = "required";

        [JsonPropertyName("server")]
        public string Server { get; init; } = "required";
    }
}
