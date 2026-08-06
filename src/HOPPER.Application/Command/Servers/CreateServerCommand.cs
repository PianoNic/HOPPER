using HOPPER.Application.Dtos.Servers;
using HOPPER.Application.Mappings.Servers;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Servers
{
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

                Token = ServerTokenGenerator.New(),
                MinecraftVersion = ServerPlatform.NormaliseVersion(command.MinecraftVersion, "Minecraft version"),
                Loader = command.Loader,
                LoaderVersion = ServerPlatform.NormaliseVersion(command.LoaderVersion, "Loader version"),
            };

            db.Servers.Add(server);
            await db.SaveChangesAsync(cancellationToken);

            return server.ToDto(modCount: 0, clientCount: 0);
        }

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
