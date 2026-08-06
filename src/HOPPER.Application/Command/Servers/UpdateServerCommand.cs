using HOPPER.Application.Dtos.Servers;
using HOPPER.Application.Mappings.Servers;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Servers
{
    /// <summary>Renaming is free; re-slugging changes the filename of every jar downloaded from here
    /// afterwards, but not the ones already on disk - the slug is a label, never an identifier
    /// anything resolves by. The token is untouched, so existing clients keep working.
    ///
    /// The platform fields are all optional and all nullable. Leaving them unset keeps the server
    /// exactly as it behaved before the browser existed - it simply cannot browse or export until
    /// they are filled in, and both of those say so.</summary>
    public record UpdateServerCommand(
        Guid Id,
        string Name,
        string Slug,
        string? MinecraftVersion = null,
        ModLoader Loader = ModLoader.Unknown,
        string? LoaderVersion = null) : ICommand<ServerDto>;

    public class UpdateServerCommandHandler(HopperDbContext db) : ICommandHandler<UpdateServerCommand, ServerDto>
    {
        public async ValueTask<ServerDto> Handle(UpdateServerCommand command, CancellationToken cancellationToken)
        {
            var name = command.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Server name is required.");

            var slug = ServerSlugValidator.Validate(command.Slug);

            var server = await db.Servers.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
                ?? throw new ServerNotFoundException(command.Id);

            if (!string.Equals(server.Slug, slug, StringComparison.Ordinal)
                && await db.Servers.AnyAsync(s => s.Slug == slug && s.Id != command.Id, cancellationToken))
            {
                throw new DuplicateServerSlugException(slug);
            }

            if (!Enum.IsDefined(command.Loader))
                throw new ArgumentException($"Unknown loader: {(int)command.Loader}.");

            server.Name = name;
            server.Slug = slug;

            // Normalised, never rejected for being absent. A server with no platform set is the
            // pre-browser default and stays perfectly usable - it simply cannot browse or export,
            // and both of those say which field is missing.
            server.MinecraftVersion = ServerPlatform.NormaliseVersion(command.MinecraftVersion, "Minecraft version");
            server.Loader = command.Loader;
            server.LoaderVersion = ServerPlatform.NormaliseVersion(command.LoaderVersion, "Loader version");

            await db.SaveChangesAsync(cancellationToken);

            var modCount = await db.Mods.CountAsync(m => m.ServerId == server.Id, cancellationToken);
            var clientCount = await db.Clients.CountAsync(c => c.ServerId == server.Id, cancellationToken);

            return server.ToDto(modCount, clientCount);
        }
    }
}
