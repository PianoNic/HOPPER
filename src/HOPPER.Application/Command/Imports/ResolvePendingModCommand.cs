using System.Security.Cryptography;
using HOPPER.Application.Dtos.Mods;
using HOPPER.Application.Mappings.Mods;
using HOPPER.Domain;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Imports
{
    /// <summary>The admin supplies the jar a pending entry was waiting for. This is Prism's
    /// BlockedModsDialog: drop the file in, it satisfies the row, the row goes away.</summary>
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

            // The pending row may carry the real filename (the CurseForge API knew it); prefer it over
            // whatever the browser called the file, so a jar renamed on the way through still lands
            // under the name the pack expects.
            var fileName = ModFileNameValidator.Validate(
                string.IsNullOrWhiteSpace(pending.FileName) ? command.FileName : pending.FileName);

            if (await db.Mods.AnyAsync(m => m.ServerId == command.ServerId && m.FileName == fileName, cancellationToken))
                throw new DuplicateModFileNameException(fileName);

            // Verified when the CurseForge API gave us a hash, taken on faith when it did not - which
            // is the keyless case, where there is genuinely nothing to check against and the admin is
            // asserting the assignment. Prism does exactly this, and compares case-insensitively.
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
            };

            db.Mods.Add(entry);
            db.PendingMods.Remove(pending);
            await db.SaveChangesAsync(cancellationToken);

            return entry.ToDto();
        }

        /// <summary>Hashes and rewinds. The stream is read twice - once here, once by the blob store -
        /// so it has to be seekable; the controller spools it if the transport did not give us one.</summary>
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
