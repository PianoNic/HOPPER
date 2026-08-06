using HOPPER.Application.Dtos.Imports;
using HOPPER.Application.Imports;
using HOPPER.Application.Mappings.Imports;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Application.Command.Imports
{
    /// <summary>Accepts a pack and hands it to the worker. Returns as soon as the bytes are on disk -
    /// a 340-file pack takes minutes and the admin is not going to hold an HTTP request open for it.
    ///
    /// An upload is staged here, on the request thread, because that is the only place its stream
    /// exists. A URL is not fetched here: the worker does that, so a slow or dead host delays one
    /// background job rather than one browser.</summary>
    public record StartPackImportCommand(
        Guid ServerId,
        ImportSourceKind SourceKind,
        string SourceName,
        Stream? Content,
        string? Url) : ICommand<ModImportDto>;

    public class StartPackImportCommandHandler(
        HopperDbContext db,
        IImportStaging staging,
        IImportQueue queue,
        ICurrentUserService currentUser,
        IConfiguration configuration) : ICommandHandler<StartPackImportCommand, ModImportDto>
    {
        private const long DefaultMaxImportBytes = 2L * 1024 * 1024 * 1024;

        public async ValueTask<ModImportDto> Handle(StartPackImportCommand command, CancellationToken cancellationToken)
        {
            if (!await db.Servers.AnyAsync(s => s.Id == command.ServerId, cancellationToken))
                throw new ServerNotFoundException(command.ServerId);

            var sourceName = command.SourceKind == ImportSourceKind.Url ? command.Url : command.SourceName;

            if (string.IsNullOrWhiteSpace(sourceName))
                throw new ArgumentException("Upload a pack file or give a URL to import from.");

            if (command.SourceKind == ImportSourceKind.Url
                && (!Uri.TryCreate(sourceName, UriKind.Absolute, out var uri)
                    || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                // https only. A pack fetched over http could be swapped in flight for one whose index
                // points wherever the attacker likes, and every hash in it would then agree.
                throw new ArgumentException("A pack URL must be an absolute https:// URL.");
            }

            if (command.SourceKind == ImportSourceKind.Upload && command.Content is null)
                throw new ArgumentException("No pack file was uploaded.");

            var import = new ModImport
            {
                ServerId = command.ServerId,
                SourceName = sourceName,
                SourceKind = command.SourceKind,
                Format = PackFormat.Unknown,
                Status = ImportStatus.Queued,
                CreatedBy = currentUser.Name,
            };

            // The row is written before the bytes are staged and before anything is queued, so an
            // import that dies at any later point is still visible and still explains itself.
            db.ModImports.Add(import);
            await db.SaveChangesAsync(cancellationToken);

            if (command.Content is not null)
            {
                try
                {
                    await staging.StageAsync(import.Id, command.Content, MaxImportBytes, cancellationToken);
                }
                catch (Exception ex)
                {
                    import.Status = ImportStatus.Failed;
                    import.Error = ex.Message;
                    import.CompletedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(CancellationToken.None);
                    throw;
                }
            }

            queue.Enqueue(import.Id);

            return import.ToDto();
        }

        private long MaxImportBytes => configuration.GetValue("Hopper:MaxImportBytes", DefaultMaxImportBytes);
    }
}
