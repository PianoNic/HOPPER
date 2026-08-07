using System.Security.Cryptography;
using HOPPER.Application.Dtos.Mods;
using HOPPER.Application.Mappings.Mods;
using HOPPER.Application.ModMetadata;
using HOPPER.Domain;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Application.Command.Imports
{
    public record ResolvePendingModCommand(Guid ServerId, Guid PendingId, string FileName, Stream Content)
        : ICommand<ModDto>;

    public class ResolvePendingModCommandHandler(
        HopperDbContext db, IBlobStorage blobs, ICurrentUserService currentUser, IConfiguration configuration)
        : ICommandHandler<ResolvePendingModCommand, ModDto>
    {
        public async ValueTask<ModDto> Handle(ResolvePendingModCommand command, CancellationToken cancellationToken)
        {
            var pending = await db.PendingMods.FirstOrDefaultAsync(
                    p => p.ServerId == command.ServerId && p.Id == command.PendingId, cancellationToken)
                ?? throw new PendingModNotFoundException(command.PendingId);

            var fileName = ModFileNameValidator.Validate(
                string.IsNullOrWhiteSpace(pending.FileName) ? command.FileName : pending.FileName);

            var lowered = fileName.ToLowerInvariant();

            if (await db.Mods.AnyAsync(m => m.ServerId == command.ServerId && m.FileName.ToLower() == lowered, cancellationToken))
                throw new DuplicateModFileNameException(fileName);

            if (!string.IsNullOrWhiteSpace(pending.ExpectedSha1))
            {
                var actual = await Sha1Async(command.Content, cancellationToken);
                if (!string.Equals(actual, pending.ExpectedSha1, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"This jar is not the file {fileName} asks for: its SHA-1 does not match.");
            }

            var staged = await blobs.StageAsync(command.Content, HopperLimits.MaxModBytes(configuration), cancellationToken);

            try
            {
                var entry = new Mod
                {
                    ServerId = command.ServerId,
                    FileName = fileName,
                    Sha256 = staged.Sha256,
                    Size = staged.Size,
                    UploadedBy = currentUser.Name,

                    ModIds = ModIdReader.FromStaged(blobs, staged),
                    IconSha256 = await ModIconStore.FromStagedJarAsync(blobs, staged, cancellationToken),
                };

                await using (var hold = await BlobLock.HoldAsync(db, staged.Sha256, cancellationToken))
                {
                    db.Mods.Add(entry);
                    db.PendingMods.Remove(pending);

                    try
                    {
                        await db.SaveChangesAsync(cancellationToken);
                    }
                    catch (DbUpdateException ex) when (ex.IsUniqueViolation())
                    {
                        db.Entry(entry).State = EntityState.Detached;
                        throw new DuplicateModFileNameException(fileName);
                    }

                    blobs.Promote(staged);
                    await hold.CommitAsync(cancellationToken);
                }

                return entry.ToDto();
            }
            finally
            {
                blobs.Discard(staged);
            }
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
