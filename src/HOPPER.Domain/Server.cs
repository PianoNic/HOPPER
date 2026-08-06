using HOPPER.Domain.Enums;

namespace HOPPER.Domain
{
    /// <summary>One row per Minecraft server HOPPER distributes to. Everything a client can see -
    /// its mod set, its manifest, its own row in the clients list - is reached through the server
    /// its bearer token resolves to, so this entity is the tenant boundary of the whole system.
    /// Blobs are the one deliberate exception: they are content-addressed and shared across all
    /// servers, because the same jar on two servers is the same bytes.</summary>
    public class Server : BaseEntity
    {
        /// <summary>Display name shown in the dashboard. Free-form and mutable; nothing keys on it.</summary>
        public required string Name { get; set; }

        /// <summary>URL- and filename-safe identifier, unique across servers. It is what makes the
        /// generated jar recognisable on disk (&lt;slug&gt;-hopper.jar), so it is constrained to
        /// lowercase alphanumerics and single dashes rather than merely being slugified on display.</summary>
        public required string Slug { get; set; }

        /// <summary>The bearer token this server's clients present, 64 lowercase hex characters.
        ///
        /// Stored in PLAINTEXT, deliberately, and this is a reversal of the usual rule for secrets.
        /// HOPPER has to be able to read it back: GET /api/servers/{id}/jar writes it into the
        /// generated jar, and the setup page reveals it for the manual fallback. A hash would make
        /// both impossible, and the token is not a user credential - it is a per-server capability
        /// the admin can rotate from the dashboard at any time.</summary>
        public required string Token { get; set; }

        /// <summary>Minecraft version this server runs, for example "1.20.1". Null on every server
        /// created before the mod browser existed, and optional afterwards.
        ///
        /// It is a plain string rather than a parsed version because Minecraft version strings are
        /// not semver - "1.20.1", "23w13a_or_b" and "1.21.1-rc1" are all real - and every consumer
        /// passes it straight through as text: the Modrinth search facet "versions:1.20.1", the
        /// .mrpack "minecraft" dependency, the CurseForge manifest's minecraft.version and the Prism
        /// net.minecraft component version are the same string in four places.</summary>
        public string? MinecraftVersion { get; set; }

        /// <summary>Which loader this server runs. Unknown means unconfigured, not "no loader".</summary>
        public ModLoader Loader { get; set; } = ModLoader.Unknown;

        /// <summary>Loader version, for example "47.4.10" for Forge on 1.20.1.
        ///
        /// Stored bare, with no Minecraft prefix: the CurseForge manifest wants "forge-47.4.10" and
        /// not "forge-1.20.1-47.4.10", and the exporters prepend whatever each format needs. Storing
        /// a prefixed value here would push the same string into a .mrpack dependency and a Prism
        /// component where it is wrong.</summary>
        public string? LoaderVersion { get; set; }
    }
}
