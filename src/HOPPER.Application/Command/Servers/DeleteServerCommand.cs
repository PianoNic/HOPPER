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
            var server = await db.Servers.FirstOrDefaultAsync(s => s.Id == command.Id, cancellationToken);
            if (server is null)
                return Unit.Value;

            var hashes = await db.Mods.AsNoTracking()
                .Where(m => m.ServerId == command.Id)
                .Select(m => m.Sha256)
                .Distinct()
                .ToListAsync(cancellationToken);

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

            await db.SaveChangesAsync(cancellationToken);

            foreach (var hash in hashes)
                await BlobCollector.CollectAsync(db, blobs, hash, cancellationToken);

            return Unit.Value;
        }
    }
}
