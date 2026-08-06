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
    /// <summary>One file in a batch. A Stream rather than a byte[] so a large content mod is written
    /// and hashed straight from the request body and never buffered by us. The caller owns the
    /// stream's lifetime.</summary>
    public sealed record UploadFile(string FileName, Stream Content);

    /// <summary>Stores any number of jars on one server in a single call, expanding any .zip in the
    /// batch into the jars it contains.
    ///
    /// This replaces the single-file upload rather than sitting beside it - a batch of one is a batch,
    /// and two code paths that both decide what a valid jar is would drift. It also replaces the FTP
    /// drop the design once considered: an admin dragging forty jars onto the page is the same act,
    /// over the transport the dashboard already speaks and the same validation everything else uses.</summary>
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

        /// <summary>Treats a .zip as a container of jars, one level deep only. Nested zips are not
        /// recursed into: a zip inside a zip is far more likely to be a modpack - which has its own
        /// import path that reads its manifest - than a second helping of loose jars.</summary>
        private async Task ExpandZipAsync(
            Guid serverId,
            UploadFile file,
            List<ModDto> uploaded,
            List<FailedUploadDto> failed,
            CancellationToken cancellationToken)
        {
            // ZipArchive needs to seek to the central directory at the end of the file. An IFormFile
            // stream is seekable, but a caller handing us a network stream is not, so it is spooled
            // first rather than failing with an opaque NotSupportedException.
            var (source, temp) = await SeekableAsync(file.Content, cancellationToken);

            try
            {
                using var archive = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);

                var jars = archive.Entries
                    .Where(e => e.Name.Length > 0                                        // not a directory entry
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
                    // Entry.Name is the basename; the directory a jar sat in inside the zip is not
                    // meaningful to a client, which puts everything flat in hoppermods/.
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

        /// <summary>The existing single-jar path, unchanged in substance: validate the name, refuse a
        /// duplicate on this server, store the bytes at their content address, insert the row.
        ///
        /// A per-file failure is recorded and the batch continues. Saving per file rather than once at
        /// the end means a batch that dies halfway leaves the jars it got through actually stored,
        /// which is what makes a retry of the same drop cheap instead of destructive.</summary>
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

                    // Read from the stored blob rather than from the upload: a jar arriving as a
                    // member of a batch zip is a DeflateStream that cannot seek, and ZipArchive has
                    // to reach the central directory at the end of the file. Reading by content
                    // address after the save is the one hook that works on every store path.
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

            // A file, not a MemoryStream: a zip of a modpack's worth of jars is measured in hundreds
            // of megabytes and holding that in the managed heap is how a small VPS falls over.
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
