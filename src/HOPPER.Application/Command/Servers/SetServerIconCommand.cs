using HOPPER.Application;
using HOPPER.Application.ModMetadata;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Servers
{
    public record SetServerIconCommand(Guid ServerId, Stream Content) : ICommand<string>;

    public class SetServerIconCommandHandler(HopperDbContext db, IBlobStorage blobs)
        : ICommandHandler<SetServerIconCommand, string>
    {
        public async ValueTask<string> Handle(SetServerIconCommand command, CancellationToken cancellationToken)
        {
            var server = await db.Servers.FirstOrDefaultAsync(s => s.Id == command.ServerId, cancellationToken)
                ?? throw new KeyNotFoundException("No such server.");

            var icon = ServerIconReader.ToServerIcon(command.Content)
                ?? throw new InvalidRequestException("That file is not an image HOPPER can read.");

            var previous = server.IconSha256;

            using var source = new MemoryStream(icon);
            var staged = await blobs.StageAsync(source, ServerIconReader.MaxUploadBytes, cancellationToken);
            blobs.Promote(staged);

            server.IconSha256 = staged.Sha256;
            await db.SaveChangesAsync(cancellationToken);

            if (previous is not null && previous != staged.Sha256)
                await BlobCollector.CollectAsync(db, blobs, previous, cancellationToken);

            return staged.Sha256;
        }
    }

    public record ClearServerIconCommand(Guid ServerId) : ICommand;

    public class ClearServerIconCommandHandler(HopperDbContext db, IBlobStorage blobs)
        : ICommandHandler<ClearServerIconCommand>
    {
        public async ValueTask<Unit> Handle(ClearServerIconCommand command, CancellationToken cancellationToken)
        {
            var server = await db.Servers.FirstOrDefaultAsync(s => s.Id == command.ServerId, cancellationToken);
            if (server?.IconSha256 is null)
                return Unit.Value;

            var previous = server.IconSha256;

            server.IconSha256 = null;
            await db.SaveChangesAsync(cancellationToken);

            await BlobCollector.CollectAsync(db, blobs, previous, cancellationToken);

            return Unit.Value;
        }
    }
}
