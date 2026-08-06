using HOPPER.Application.Dtos.Servers;
using HOPPER.Application.Mappings.Servers;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Servers
{
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
