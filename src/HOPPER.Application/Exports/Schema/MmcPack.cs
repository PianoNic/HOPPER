using System.Text.Json.Serialization;

namespace HOPPER.Application.Exports.Schema
{
    /// <summary>mmc-pack.json of a Prism / MultiMC instance.
    ///
    /// An external FILE FORMAT contract. Only formatVersion and each component's uid and version are
    /// required; everything a real instance additionally carries with a "cached" prefix is a local
    /// resolution cache Prism rebuilds from meta.prismlauncher.org on first launch, and org.lwjgl3 is
    /// pulled in automatically as a dependency of net.minecraft. None of that is emitted, because
    /// writing a stale cache is worse than writing none.</summary>
    public sealed record MmcPack
    {
        [JsonPropertyName("formatVersion")]
        public int FormatVersion { get; init; } = 1;

        [JsonPropertyName("components")]
        public required IReadOnlyList<MmcComponent> Components { get; init; }
    }

    public sealed record MmcComponent
    {
        [JsonPropertyName("uid")]
        public required string Uid { get; init; }

        [JsonPropertyName("version")]
        public required string Version { get; init; }

        /// <summary>Set on net.minecraft only. Omitted rather than written false elsewhere, which is
        /// how a real instance file reads.</summary>
        [JsonPropertyName("important")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Important { get; init; }
    }
}
