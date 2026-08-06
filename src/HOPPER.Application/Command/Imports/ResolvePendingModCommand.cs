using System.Security.Cryptography;
using HOPPER.Application.Dtos.Mods;
using HOPPER.Application.Mappings.Mods;
using HOPPER.Application.ModMetadata;
using HOPPER.Domain;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Imports
{
    public record ResolvePendingModCommand(Guid ServerId, Guid PendingId, string FileName, Stream Content)
        : ICommand<ModDto>;

    public class ResolvePendingModCommandHandler(HopperDbContext db, IBlobStorage blobs, ICurrentUserService currentUser)
        : ICommandHandler<ResolvePendingModCommand, ModDto>
    {
        public async ValueTask<ModDto> Handle(ResolvePendingModCommand command, CancellationToken cancellationToken)
        {
            var pending = await db.PendingMods.FirstOrDefaultAsync(
                    p => p.ServerId == command.ServerId && p.Id == command.PendingId, cancellationToken)
                ?? throw new PendingModNotFoundException(command.PendingId);

            var fileName = ModFileNameValidator.Validate(
                string.IsNullOrWhiteSpace(pending.FileName) ? command.FileName : pending.FileName);

            if (await db.Mods.AnyAsync(m => m.ServerId == command.ServerId && m.FileName == fileName, cancellationToken))
                throw new DuplicateModFileNameException(fileName);

            if (!string.IsNullOrWhiteSpace(pending.ExpectedSha1))
            {
                var actual = await Sha1Async(command.Content, cancellationToken);
                if (!string.Equals(actual, pending.ExpectedSha1, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"This jar is not the file {fileName} asks for: its SHA-1 does not match.");
            }

            var (sha256, size) = await blobs.SaveAsync(command.Content, cancellationToken);

            var entry = new Mod
            {
                ServerId = command.ServerId,
                FileName = fileName,
                Sha256 = sha256,
                Size = size,
                UploadedBy = currentUser.Name,

                ModIds = ModIdReader.FromBlob(blobs, sha256),
            };

            db.Mods.Add(entry);
            db.PendingMods.Remove(pending);
            await db.SaveChangesAsync(cancellationToken);

            return entry.ToDto();
        }

        private static async Task<string> Sha1Async(Stream content, CancellationToken cancellationToken)
        {
            if (!content.CanSeek)
                throw new ArgumentException("The supplied jar could not be re-read for verification.");

            content.Position = 0;
            var hash = await SHA1.HashDataAsync(content, cancellationToken);
            content.Position = 0;
            return Convert.ToHexStringLower(hash);
        }
    }

    public sealed class PendingModNotFoundException(Guid id)
        : InvalidOperationException($"No pending mod with id {id} on this server.")
    {
        public Guid PendingId { get; } = id;
    }
}
