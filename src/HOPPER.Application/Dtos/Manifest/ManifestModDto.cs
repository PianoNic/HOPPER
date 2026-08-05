using System.Text.Json.Serialization;

namespace HOPPER.Application.Dtos.Manifest
{
    /// <summary>One entry in the manifest the Forge locator consumes. The four names below are a
    /// fixed, already-shipped contract with the Java client, so every one carries an explicit
    /// [JsonPropertyName]. The ASP.NET default naming policy would happen to produce the same
    /// strings today, but that is a coincidence of capitalisation rather than a guarantee: rename
    /// this property to SHA256 and camelCase emits "shA256", which the client reads as a null hash
    /// and then re-downloads every jar on every launch, with no compiler error to warn anyone.
    /// Do not rename, do not add properties, do not make any of them nullable.</summary>
    public record ManifestModDto
    {
        [JsonPropertyName("file")] public required string File { get; init; }

        [JsonPropertyName("url")] public required string Url { get; init; }

        [JsonPropertyName("sha256")] public required string Sha256 { get; init; }

        /// <summary>Byte count. Must serialise as a JSON number: the Java Entry.size field is a
        /// primitive long, and a quoted value fails Gson's parse of the whole entry.</summary>
        [JsonPropertyName("size")] public required long Size { get; init; }
    }
}
