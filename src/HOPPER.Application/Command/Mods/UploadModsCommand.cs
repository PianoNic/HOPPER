using HOPPER.Application.Modrinth;
using System.IO.Compression;
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

namespace HOPPER.Application.Command.Mods
{
    public sealed record UploadFile(string FileName, Stream Content);

    public record UploadModsCommand(Guid ServerId, IReadOnlyList<UploadFile> Files) : ICommand<ModUploadResultDto>;

    public class UploadModsCommandHandler(
        HopperDbContext db, IBlobStorage blobs, ICurrentUserService currentUser, IConfiguration configuration)
        : ICommandHandler<UploadModsCommand, ModUploadResultDto>
    {
        public async ValueTask<ModUploadResultDto> Handle(UploadModsCommand command, CancellationToken cancellationToken)
        {
            var uploaded = new List<ModDto>();
            var failed = new List<FailedUploadDto>();

            foreach (var file in command.Files)
            {
                if (IsZip(file.FileName))
                    await ExpandZipAsync(command.ServerId, file, uploaded, failed, cancellationToken);
                else
                    await StoreAsync(command.ServerId, file.FileName, file.Content, uploaded, failed, cancellationToken);
            }

            return new ModUploadResultDto { Uploaded = uploaded, Failed = failed };
        }

        private static bool IsZip(string name) => name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

        private async Task ExpandZipAsync(
            Guid serverId,
            UploadFile file,
            List<ModDto> uploaded,
            List<FailedUploadDto> failed,
            CancellationToken cancellationToken)
        {
            var (source, temp) = await SeekableAsync(file.Content, HopperLimits.MaxImportBytes(configuration), cancellationToken);

            try
            {
                using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);

                var jars = archive.Entries
                    .Where(e => e.Name.Length > 0
                                && e.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
                                && !e.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal))
                    .ToList();

                if (jars.Count == 0)
                {
                    failed.Add(new FailedUploadDto { FileName = file.FileName, Error = "The archive contains no jars." });
                    return;
                }

                foreach (var entry in jars)
                {
                    await using var content = entry.Open();
                    await StoreAsync(serverId, entry.Name, content, uploaded, failed, cancellationToken);
                }
            }
            catch (InvalidDataException)
            {
                failed.Add(new FailedUploadDto { FileName = file.FileName, Error = "Not a readable zip archive." });
            }
            finally
            {
                if (temp is not null)
                    await temp.DisposeAsync();
            }
        }

        private async Task StoreAsync(
            Guid serverId,
            string fileName,
            Stream content,
            List<ModDto> uploaded,
            List<FailedUploadDto> failed,
            CancellationToken cancellationToken)
        {
            StagedBlob? staged = null;

            try
            {
                var validated = ModFileNameValidator.Validate(fileName);
                var lowered = validated.ToLowerInvariant();

                if (await db.Mods.AnyAsync(m => m.ServerId == serverId && m.FileName.ToLower() == lowered, cancellationToken))
                    throw new DuplicateModFileNameException(validated);

                // Hashed on the way in so an uploaded jar can be recognised on Modrinth later. The
                // stream is read once either way; leaveOpen because the caller owns it.
                await using var hashing = new HashingStream(content, leaveOpen: true);

                staged = await blobs.StageAsync(hashing, HopperLimits.MaxModBytes(configuration), cancellationToken);

                var metadata = await ModJarReader.FromStagedAsync(blobs, staged, cancellationToken);

                await ModIdConflictValidator.RefuseIfClaimedAsync(
                    db, serverId, metadata.ModIds, metadata.Side, cancellationToken: cancellationToken);

                var entry = new Mod
                {
                    ServerId = serverId,
                    FileName = validated,
                    Sha256 = staged.Sha256,
                    Size = staged.Size,
                    UploadedBy = currentUser.Name,

                    Side = metadata.Side,

                    ModIds = metadata.ModIds,
                    RequiredMods = metadata.RequiredMods,
                    IconSha256 = metadata.IconSha256,

                    Sha1 = hashing.Sha1Hex,
                    Sha512 = hashing.Sha512Hex,
                };

                db.Mods.Add(entry);

                if (await BlobLock.SaveWithBlobAsync(db, blobs, staged, cancellationToken) is BlobSaveOutcome.Duplicate)
                {
                    db.Entry(entry).State = EntityState.Detached;
                    throw new DuplicateModFileNameException(validated);
                }

                uploaded.Add(entry.ToDto());
            }
            catch (DuplicateModFileNameException ex)
            {
                failed.Add(new FailedUploadDto { FileName = fileName, Error = ex.Message });
            }
            catch (RuleViolationException ex)
            {
                failed.Add(new FailedUploadDto { FileName = fileName, Error = ex.Message });
            }
            finally
            {
                if (staged is not null)
                    blobs.Discard(staged);
            }
        }

        private static async Task<(Stream Stream, IAsyncDisposable? Temp)> SeekableAsync(
            Stream source, long maxBytes, CancellationToken cancellationToken)
        {
            if (source.CanSeek)
            {
                source.Position = 0;
                return (source, null);
            }

            var temp = new FileStream(
                Path.Combine(Path.GetTempPath(), $"hopper-zip-{Guid.NewGuid():N}.tmp"),
                FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);

            try
            {
                await new LimitedStream(source, maxBytes, "The archive").CopyToAsync(temp, cancellationToken);
            }
            catch
            {
                await temp.DisposeAsync();
                throw;
            }

            temp.Position = 0;
            return (temp, temp);
        }
    }
}
