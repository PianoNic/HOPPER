using System.Text.Json.Serialization;

namespace HOPPER.Application.Dtos.Manifest
{
    /// <summary>One entry in the manifest the Forge locator consumes. The four names below are a
    /// fixed, already-shipped contract with the Java client, so every one carries an explicit
    /// [JsonPropertyName]. The ASP.NET default naming policy would happen to produce the same
    /// strings today, but that is a coincidence of capitalisation rather than a guarantee: rename
    /// this property to SHA256 and camelCase emits "shA256", which the client reads as a null hash
    /// and then re-downloads every jar on every launch, with no compiler error to warn anyone.
    /// Do not rename the four names below, do not reorder them, and do not make any of them
    /// nullable.
    ///
    /// ADDITIVE FIELDS ARE ALLOWED, appended after "size" and omitted from the JSON when they have
    /// no value. "modIds" is the first one. The shipped client parses this with a hand-written
    /// reader that looks up the keys it knows by name and ignores everything else, so an extra key
    /// is inert to every jar already in the field - which is only true while the four originals keep
    /// their names, their order and their types. That is what the tests in HOPPER.Tests/Wire pin,
    /// and they pass unchanged.</summary>
    public record ManifestModDto
    {
        [JsonPropertyName("file")] public required string File { get; init; }

        [JsonPropertyName("url")] public required string Url { get; init; }

        [JsonPropertyName("sha256")] public required string Sha256 { get; init; }

        /// <summary>Byte count. Must serialise as a JSON number: the Java Entry.size field is a
        /// primitive long, and a quoted value fails Gson's parse of the whole entry.</summary>
        [JsonPropertyName("size")] public required long Size { get; init; }

        /// <summary>Every mod id this jar declares. The client reads the same ids out of the jars in
        /// the player's own mods/ folder and uses this to recognise the same mod under a different
        /// filename and a different hash, which is the one thing filename and sha256 cannot do.
        ///
        /// Declared LAST so the four bytes-on-the-wire fields above never move, and omitted
        /// entirely - never emitted as null, never as [] - when the jar declares none or the row has
        /// not been read yet. There is nothing a client can do with either case, and omitting keeps
        /// the shape byte-identical for every jar that is not a mod.</summary>
        [JsonPropertyName("modIds")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IReadOnlyList<string>? ModIds { get; init; }
    }
}
