using System.IO.Compression;
using HOPPER.Application.Dtos.Mods;
using HOPPER.Application.Mappings.Mods;
using HOPPER.Application.ModMetadata;
using HOPPER.Domain;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Mods
{
    public sealed record UploadFile(string FileName, Stream Content);

    public record UploadModsCommand(Guid ServerId, IReadOnlyList<UploadFile> Files) : ICommand<ModUploadResultDto>;

    public class UploadModsCommandHandler(HopperDbContext db, IBlobStorage blobs, ICurrentUserService currentUser)
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
            var (source, temp) = await SeekableAsync(file.Content, cancellationToken);

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
            try
            {
                var validated = ModFileNameValidator.Validate(fileName);

                if (await db.Mods.AnyAsync(m => m.ServerId == serverId && m.FileName == validated, cancellationToken))
                    throw new DuplicateModFileNameException(validated);

                var (sha256, size) = await blobs.SaveAsync(content, cancellationToken);

                var entry = new Mod
                {
                    ServerId = serverId,
                    FileName = validated,
                    Sha256 = sha256,
                    Size = size,
                    UploadedBy = currentUser.Name,

                    ModIds = ModIdReader.FromBlob(blobs, sha256),
                };

                db.Mods.Add(entry);
                await db.SaveChangesAsync(cancellationToken);

                uploaded.Add(entry.ToDto());
            }
            catch (DuplicateModFileNameException ex)
            {
                failed.Add(new FailedUploadDto { FileName = fileName, Error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                failed.Add(new FailedUploadDto { FileName = fileName, Error = ex.Message });
            }
        }

        private static async Task<(Stream Stream, IAsyncDisposable? Temp)> SeekableAsync(Stream source, CancellationToken cancellationToken)
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

            await source.CopyToAsync(temp, cancellationToken);
            temp.Position = 0;
            return (temp, temp);
        }
    }
}
