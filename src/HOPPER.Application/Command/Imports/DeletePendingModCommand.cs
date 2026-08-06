using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Imports
{
    public record DeletePendingModCommand(Guid ServerId, Guid PendingId) : ICommand;

    public class DeletePendingModCommandHandler(HopperDbContext db) : ICommandHandler<DeletePendingModCommand>
    {
        public async ValueTask<Unit> Handle(DeletePendingModCommand command, CancellationToken cancellationToken)
        {
            var pending = await db.PendingMods.FirstOrDefaultAsync(
                p => p.ServerId == command.ServerId && p.Id == command.PendingId, cancellationToken);

            if (pending is not null)
            {
                db.PendingMods.Remove(pending);
                await db.SaveChangesAsync(cancellationToken);
            }

            return Unit.Value;
        }
    }
}
