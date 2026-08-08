using HOPPER.Application;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Mods
{
    public record SetModSideCommand(Guid ServerId, IReadOnlyList<Guid> ModIds, ModSide Side) : ICommand<int>;

    public class SetModSideCommandHandler(HopperDbContext db) : ICommandHandler<SetModSideCommand, int>
    {
        public async ValueTask<int> Handle(SetModSideCommand command, CancellationToken cancellationToken)
        {
            if (!Enum.IsDefined(command.Side))
                throw new InvalidRequestException($"Unknown side: {(int)command.Side}.");

            if (command.ModIds.Count == 0)
                return 0;

            var ids = command.ModIds.Distinct().ToList();

            var matched = await db.Mods
                .Where(m => m.ServerId == command.ServerId && ids.Contains(m.Id))
                .ToListAsync(cancellationToken);

            if (matched.Count == 0)
                return 0;

            // A side change is the one path that can create a clash without downloading anything:
            // a Client only and a Server only copy of one mod are legal until either becomes Both.
            foreach (var mod in matched)
            {
                await ModIdConflictValidator.RefuseIfClaimedAsync(
                    db, command.ServerId, mod.ModIds, command.Side, mod.Id, cancellationToken);
            }

            foreach (var mod in matched)
                mod.Side = command.Side;

            await db.SaveChangesAsync(cancellationToken);
            return matched.Count;
        }
    }
}
