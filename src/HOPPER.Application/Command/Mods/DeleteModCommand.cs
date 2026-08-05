using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Mods
{
    public record DeleteModCommand(Guid Id) : ICommand;

    public class DeleteModCommandHandler(HopperDbContext db, IBlobStorage blobs) : ICommandHandler<DeleteModCommand>
    {
        public async ValueTask<Unit> Handle(DeleteModCommand command, CancellationToken cancellationToken)
        {
            // Idempotent: deleting an id that is already gone is a no-op, so a retried request from a
            // dashboard that lost the response does not turn into a 404 the admin has to interpret.
            var entry = await db.Mods.FirstOrDefaultAsync(m => m.Id == command.Id, cancellationToken);
            if (entry is not null)
            {
                db.Mods.Remove(entry);
                await db.SaveChangesAsync(cancellationToken);

                // Content-addressed storage means two mods can legitimately share one blob (the same
                // jar published under two names). Only the last reference may take the file with it.
                var stillReferenced = await db.Mods.AnyAsync(m => m.Sha256 == entry.Sha256, cancellationToken);
                if (!stillReferenced)
                    blobs.Delete(entry.Sha256);
            }

            return Unit.Value;
        }
    }
}
