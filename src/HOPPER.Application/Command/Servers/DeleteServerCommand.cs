using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Servers
{
    public record DeleteServerCommand(Guid Id) : ICommand;

    public class DeleteServerCommandHandler(HopperDbContext db, IBlobStorage blobs)
        : ICommandHandler<DeleteServerCommand>
    {
        public async ValueTask<Unit> Handle(DeleteServerCommand command, CancellationToken cancellationToken)
        {
            // Idempotent, like every other delete here: a retried request from a dashboard that lost
            // the response must not turn into a 404 the admin has to interpret.
            var server = await db.Servers.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken);
            if (server is null)
                return Unit.Value;

            // Collected BEFORE the rows go, because after the delete there is nothing left to ask
            // which blobs this server was holding on to.
            var hashes = await db.Mods.AsNoTracking()
                .Where(m => m.ServerId == command.Id)
                .Select(m => m.Sha256)
                .Distinct()
                .ToListAsync(cancellationToken);

            // ClientReportedMod hangs off Client.Id, not off the server, so it has to be reached
            // through this server's client ids rather than by a column of its own.
            var clientIds = await db.Clients.AsNoTracking()
                .Where(c => c.ServerId == command.Id)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);

            db.ClientReportedMods.RemoveRange(
                await db.ClientReportedMods.Where(r => clientIds.Contains(r.ClientId)).ToListAsync(cancellationToken));
            db.Clients.RemoveRange(
                await db.Clients.Where(c => c.ServerId == command.Id).ToListAsync(cancellationToken));
            db.PendingMods.RemoveRange(
                await db.PendingMods.Where(p => p.ServerId == command.Id).ToListAsync(cancellationToken));
            db.ModImports.RemoveRange(
                await db.ModImports.Where(i => i.ServerId == command.Id).ToListAsync(cancellationToken));
            db.Mods.RemoveRange(
                await db.Mods.Where(m => m.ServerId == command.Id).ToListAsync(cancellationToken));
            db.Servers.Remove(server);

            // One save: a half-deleted server whose token still resolves but whose mods are gone would
            // hand every one of its clients an empty manifest, and an empty manifest is an instruction
            // to delete every jar in hoppermods/.
            await db.SaveChangesAsync(cancellationToken);

            // Same global orphan check DeleteModCommand runs, once per hash this server released. No
            // ServerId filter, deliberately: a blob shared with another server must survive, and
            // narrowing this is the single mistake that would empty someone else's mod set.
            foreach (var hash in hashes)
            {
                var stillReferenced = await db.Mods.AnyAsync(m => m.Sha256 == hash, cancellationToken);
                if (!stillReferenced)
                    blobs.Delete(hash);
            }

            return Unit.Value;
        }
    }
}
