using HOPPER.Application.Dtos.Clients;
using HOPPER.Domain;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Clients
{
    public record RecordClientReportCommand(ClientReportDto Body, string? IpAddress) : ICommand;

    public class RecordClientReportCommandHandler(HopperDbContext db) : ICommandHandler<RecordClientReportCommand>
    {
        public async ValueTask<Unit> Handle(RecordClientReportCommand command, CancellationToken cancellationToken)
        {
            var body = command.Body;

            if (string.IsNullOrWhiteSpace(body.ClientId))
                throw new ArgumentException("clientId is required.");

            // A username is genuinely optional: a dedicated server, and any launcher started without
            // --username, has none and sends null. Blank and whitespace are folded into null too, so
            // the dashboard has exactly one "no username" state to render instead of three.
            var username = string.IsNullOrWhiteSpace(body.Username) ? null : body.Username.Trim();

            // There is no registration step: a client exists exactly when it has reported once, so
            // the first report creates the row and every later one refreshes it.
            var client = await db.Clients.FirstOrDefaultAsync(c => c.ClientId == body.ClientId, cancellationToken);
            if (client is null)
            {
                client = new Client
                {
                    ClientId = body.ClientId,
                    Username = username,
                    LastSeenAt = DateTime.UtcNow,
                    LastIpAddress = command.IpAddress,
                };
                db.Clients.Add(client);
            }
            else
            {
                client.Username = username;
                client.LastSeenAt = DateTime.UtcNow;
                client.LastIpAddress = command.IpAddress;
            }

            // The reported set is replaced wholesale rather than merged: the report describes the
            // client's disk as of now, and anything not in it is by definition no longer there.
            var previous = await db.ClientReportedMods
                .Where(r => r.ClientId == client.Id)
                .ToListAsync(cancellationToken);
            db.ClientReportedMods.RemoveRange(previous);

            foreach (var mod in body.Mods)
            {
                if (string.IsNullOrWhiteSpace(mod.File) || string.IsNullOrWhiteSpace(mod.Sha256))
                    throw new ArgumentException("Each reported mod needs a file and a sha256.");

                db.ClientReportedMods.Add(new ClientReportedMod
                {
                    ClientId = client.Id,
                    FileName = mod.File,
                    Sha256 = mod.Sha256,
                });
            }

            // One save for the delete and the inserts together, so a client is never left with a
            // half-replaced set if the request dies midway.
            await db.SaveChangesAsync(cancellationToken);

            return Unit.Value;
        }
    }
}
