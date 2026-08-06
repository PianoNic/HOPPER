using HOPPER.Application.Dtos.Servers;
using HOPPER.Application.Mappings.Servers;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Servers
{
    public record RotateServerTokenCommand(Guid Id) : ICommand<ServerTokenDto>;

    public class RotateServerTokenCommandHandler(HopperDbContext db)
        : ICommandHandler<RotateServerTokenCommand, ServerTokenDto>
    {
        public async ValueTask<ServerTokenDto> Handle(RotateServerTokenCommand command, CancellationToken cancellationToken)
        {
            var server = await db.Servers.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken)
                ?? throw new ServerNotFoundException(command.Id);

            server.Token = ServerTokenGenerator.New();
            await db.SaveChangesAsync(cancellationToken);

            return server.ToTokenDto();
        }
    }
}
