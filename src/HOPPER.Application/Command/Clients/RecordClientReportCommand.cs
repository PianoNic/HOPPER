using HOPPER.Application.Dtos.Clients;
using HOPPER.Domain;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Clients
{
    public record RecordClientReportCommand(Guid ServerId, ClientReportDto Body, string? IpAddress) : ICommand;

    public class RecordClientReportCommandHandler(HopperDbContext db) : ICommandHandler<RecordClientReportCommand>
    {
        public async ValueTask<Unit> Handle(RecordClientReportCommand command, CancellationToken cancellationToken)
        {
            var body = command.Body;

            if (string.IsNullOrWhiteSpace(body.ClientId))
                throw new ArgumentException("clientId is required.");

            var username = string.IsNullOrWhiteSpace(body.Username) ? null : body.Username.Trim();

            var client = await db.Clients.FirstOrDefaultAsync(
                c => c.ServerId == command.ServerId && c.ClientId == body.ClientId, cancellationToken);

            if (client is null)
            {
                client = new Client
                {
                    ServerId = command.ServerId,
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

            await db.SaveChangesAsync(cancellationToken);

            return Unit.Value;
        }
    }
}
