using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Mods
{
    public record DeleteModCommand(Guid ServerId, Guid Id) : ICommand;

    public class DeleteModCommandHandler(HopperDbContext db, IBlobStorage blobs) : ICommandHandler<DeleteModCommand>
    {
        public async ValueTask<Unit> Handle(DeleteModCommand command, CancellationToken cancellationToken)
        {
            var entry = await db.Mods.FirstOrDefaultAsync(
                m => m.ServerId == command.ServerId && m.Id == command.Id, cancellationToken);

            if (entry is not null)
            {
                db.Mods.Remove(entry);
                await db.SaveChangesAsync(cancellationToken);

                var stillReferenced = await db.Mods.AnyAsync(m => m.Sha256 == entry.Sha256, cancellationToken);
                if (!stillReferenced)
                    blobs.Delete(entry.Sha256);
            }

            return Unit.Value;
        }
    }
}
