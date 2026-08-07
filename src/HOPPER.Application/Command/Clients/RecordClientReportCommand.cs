using System.Buffers;
using HOPPER.Application.Dtos.Clients;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Application.Command.Clients
{
    public record RecordClientReportCommand(Guid ServerId, ClientReportDto Body, string? IpAddress) : ICommand;

    public class RecordClientReportCommandHandler(HopperDbContext db, IConfiguration configuration)
        : ICommandHandler<RecordClientReportCommand>
    {
        private static readonly SearchValues<char> HexChars = SearchValues.Create("0123456789abcdefABCDEF");

        public async ValueTask<Unit> Handle(RecordClientReportCommand command, CancellationToken cancellationToken)
        {
            var body = command.Body;

            if (string.IsNullOrWhiteSpace(body.ClientId))
                throw new ArgumentException("clientId is required.");

            if (body.ClientId.Length > HopperLimits.MaxClientIdLength)
                throw new ArgumentException($"clientId is longer than {HopperLimits.MaxClientIdLength} characters.");

            var maxMods = HopperLimits.MaxReportedMods(configuration);
            if (body.Mods.Count > maxMods)
                throw new ArgumentException($"A client may report at most {maxMods} mods.");

            var username = string.IsNullOrWhiteSpace(body.Username) ? null : body.Username.Trim();

            if (username is { Length: > HopperLimits.MaxUsernameLength })
                username = username[..HopperLimits.MaxUsernameLength];

            // An unrecognised value is client rather than a 400: this arrives from a jar on a
            // machine nobody controls, and refusing the whole report over one field would lose the
            // mod list too.
            ModSideRules.TryParse(body.Side, out var side);

            var client = await db.Clients.FirstOrDefaultAsync(
                c => c.ServerId == command.ServerId && c.ClientId == body.ClientId, cancellationToken);

            if (client is null)
            {
                client = new Client
                {
                    ServerId = command.ServerId,
                    ClientId = body.ClientId,
                    Username = username,
                    Side = side,
                    LastSeenAt = DateTime.UtcNow,
                    LastIpAddress = command.IpAddress,
                };
                db.Clients.Add(client);
            }
            else
            {
                client.Username = username;
                client.Side = side;
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

                var fileName = ModFileNameValidator.Validate(mod.File);

                if (!IsSha256(mod.Sha256))
                    throw new ArgumentException($"{fileName} was reported with something that is not a sha256.");

                db.ClientReportedMods.Add(new ClientReportedMod
                {
                    ClientId = client.Id,
                    FileName = fileName,
                    Sha256 = mod.Sha256.ToLowerInvariant(),
                });
            }

            await db.SaveChangesAsync(cancellationToken);

            return Unit.Value;
        }

        private static bool IsSha256(string value) =>
            value.Length == 64 && !value.AsSpan().ContainsAnyExcept(HexChars);
    }
}
