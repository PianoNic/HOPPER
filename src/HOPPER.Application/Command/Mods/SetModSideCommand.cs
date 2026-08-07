using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Mods
{
    /// <summary>
    /// Takes a list rather than one id because a pack import routinely produces thirty client-only
    /// mods to reclassify, and thirty round trips is not a workflow.
    /// </summary>
    public record SetModSideCommand(Guid ServerId, IReadOnlyList<Guid> ModIds, ModSide Side) : ICommand<int>;

    public class SetModSideCommandHandler(HopperDbContext db) : ICommandHandler<SetModSideCommand, int>
    {
        public async ValueTask<int> Handle(SetModSideCommand command, CancellationToken cancellationToken)
        {
            if (!Enum.IsDefined(command.Side))
                throw new ArgumentException($"Unknown side: {(int)command.Side}.");

            if (command.ModIds.Count == 0)
                return 0;

            var ids = command.ModIds.Distinct().ToList();

            // Scoped to the server the route named, so an id belonging to another server matches
            // nothing rather than being reassigned across the tenant boundary.
            var matched = await db.Mods
                .Where(m => m.ServerId == command.ServerId && ids.Contains(m.Id))
                .ToListAsync(cancellationToken);

            if (matched.Count == 0)
                return 0;

            foreach (var mod in matched)
                mod.Side = command.Side;

            await db.SaveChangesAsync(cancellationToken);
            return matched.Count;
        }
    }
}
