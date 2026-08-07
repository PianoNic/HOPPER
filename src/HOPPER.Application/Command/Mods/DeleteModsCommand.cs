using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Mods
{
    public record DeleteModsCommand(Guid ServerId, IReadOnlyList<Guid> ModIds) : ICommand<int>;

    public class DeleteModsCommandHandler(HopperDbContext db, IBlobStorage blobs) : ICommandHandler<DeleteModsCommand, int>
    {
        public async ValueTask<int> Handle(DeleteModsCommand command, CancellationToken cancellationToken)
        {
            if (command.ModIds.Count == 0)
                return 0;

            var ids = command.ModIds.Distinct().ToList();

            var matched = await db.Mods
                .Where(m => m.ServerId == command.ServerId && ids.Contains(m.Id))
                .ToListAsync(cancellationToken);

            if (matched.Count == 0)
                return 0;

            var hashes = matched.Select(m => m.Sha256).Distinct().ToList();

            db.Mods.RemoveRange(matched);
            await db.SaveChangesAsync(cancellationToken);

            // After the rows are gone and once per distinct hash: two selected mods can share a blob,
            // and collecting while a row still referenced it would find itself and keep the file.
            foreach (var sha256 in hashes)
                await BlobCollector.CollectAsync(db, blobs, sha256, cancellationToken);

            return matched.Count;
        }
    }
}
