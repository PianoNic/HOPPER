using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Imports
{
    /// <summary>Dismisses a pending entry the admin has decided not to supply - a client-only mod
    /// they do not want, or one they have given up finding. Idempotent, and scoped to the server so a
    /// pending id from elsewhere is a no-op rather than a cross-tenant delete.</summary>
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
