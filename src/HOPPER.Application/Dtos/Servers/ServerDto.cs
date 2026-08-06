using HOPPER.Domain.Enums;

namespace HOPPER.Application.Dtos.Servers
{
    /// <summary>Admin view of one server. Deliberately carries no token: the token is a credential
    /// that opens that server's whole mod set, and a list endpoint the dashboard calls on every page
    /// load is the wrong place to hand it out. It has its own endpoint and its own DTO
    /// (<see cref="ServerTokenDto"/>) so revealing it is always a deliberate act.</summary>
    public record ServerDto
    {
        public required Guid Id { get; init; }
        public required string Name { get; init; }

        /// <summary>Names the generated jar (&lt;slug&gt;-hopper.jar), which is why it is unique and
        /// filename-safe rather than merely a display convenience.</summary>
        public required string Slug { get; init; }

        /// <summary>Mods on this server. Counted rather than embedded - the list page shows a number
        /// and the mods page fetches the rows.</summary>
        public required int ModCount { get; init; }

        public required int ClientCount { get; init; }
        public required DateTime CreatedAt { get; init; }

        /// <summary>Null until an admin sets it. The browser and the pack export both refuse to run
        /// without it, and say so rather than filtering on a guess.</summary>
        public string? MinecraftVersion { get; init; }

        /// <summary>0 Unknown, 1 Forge, 2 NeoForge, 3 Fabric, 4 Quilt. Mirrored by number on the
        /// frontend.</summary>
        public required ModLoader Loader { get; init; }

        /// <summary>Bare, with no Minecraft prefix - "47.4.10", not "1.20.1-47.4.10". Each exporter
        /// prepends whatever its own format wants.</summary>
        public string? LoaderVersion { get; init; }
    }
}
