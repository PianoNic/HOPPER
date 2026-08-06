using HOPPER.Application.Dtos.Servers;
using HOPPER.Application.Mappings.Servers;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Servers
{
    /// <summary>Slug is optional: the dashboard's create dialog asks for a name and lets the slug be
    /// derived, because an admin naming "Friday Night SMP" should not also have to invent
    /// "friday-night-smp". Supplying one explicitly is still allowed, and then it is taken literally.
    ///
    /// The platform fields are optional at creation for the same reason they are nullable on the
    /// entity: an admin creating a server has not necessarily decided which Forge build it runs, and
    /// making them mandatory here would break the one-field create dialog that already exists.</summary>
    public record CreateServerCommand(
        string Name,
        string? Slug,
        string? MinecraftVersion = null,
        ModLoader Loader = ModLoader.Unknown,
        string? LoaderVersion = null) : ICommand<ServerDto>;

    public class CreateServerCommandHandler(HopperDbContext db) : ICommandHandler<CreateServerCommand, ServerDto>
    {
        public async ValueTask<ServerDto> Handle(CreateServerCommand command, CancellationToken cancellationToken)
        {
            var name = command.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Server name is required.");

            string slug;
            if (!string.IsNullOrWhiteSpace(command.Slug))
            {
                // Explicit: the admin typed it, so a collision is a mistake they should see rather
                // than something we silently rename behind their back.
                slug = ServerSlugValidator.Validate(command.Slug);
                if (await db.Servers.AnyAsync(s => s.Slug == slug, cancellationToken))
                    throw new DuplicateServerSlugException(slug);
            }
            else
            {
                var derived = ServerSlugValidator.Derive(name)
                    ?? throw new ArgumentException($"No slug can be derived from \"{name}\". Supply one explicitly.");
                slug = await UniqueAsync(derived, cancellationToken);
            }

            if (!Enum.IsDefined(command.Loader))
                throw new ArgumentException($"Unknown loader: {(int)command.Loader}.");

            var server = new Server
            {
                Name = name,
                Slug = slug,
                // Minted here rather than accepted from the caller: a token the admin chooses is a
                // token as strong as an admin's imagination, and this one is what stands between the
                // internet and the mod set.
                Token = ServerTokenGenerator.New(),
                MinecraftVersion = ServerPlatform.NormaliseVersion(command.MinecraftVersion, "Minecraft version"),
                Loader = command.Loader,
                LoaderVersion = ServerPlatform.NormaliseVersion(command.LoaderVersion, "Loader version"),
            };

            db.Servers.Add(server);
            await db.SaveChangesAsync(cancellationToken);

            return server.ToDto(modCount: 0, clientCount: 0);
        }

        /// <summary>Appends -2, -3, … to a derived slug until one is free. Only ever applied to a
        /// derived slug: silently renaming what the admin typed would leave them looking for a server
        /// under a name that does not exist.</summary>
        private async Task<string> UniqueAsync(string candidate, CancellationToken cancellationToken)
        {
            var taken = await db.Servers
                .Where(s => s.Slug == candidate || s.Slug.StartsWith(candidate + "-"))
                .Select(s => s.Slug)
                .ToListAsync(cancellationToken);

            var used = taken.ToHashSet(StringComparer.Ordinal);
            if (!used.Contains(candidate))
                return candidate;

            for (var n = 2; n <= 99; n++)
            {
                var suffix = $"-{n}";
                // Truncate the stem, not the suffix: "…-2" that overflows the length limit would
                // otherwise silently become the same slug as "…" and collide again.
                var stem = candidate.Length + suffix.Length > ServerSlugValidator.MaxLength
                    ? candidate[..(ServerSlugValidator.MaxLength - suffix.Length)].TrimEnd('-')
                    : candidate;

                var next = stem + suffix;
                if (!used.Contains(next))
                    return next;
            }

            throw new DuplicateServerSlugException(candidate);
        }
    }
}
