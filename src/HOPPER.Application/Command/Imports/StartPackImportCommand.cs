using HOPPER.Application;
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
        public async ValueTask<ModImportDto> Handle(StartPackImportCommand command, CancellationToken cancellationToken)
        {
            if (!await db.Servers.AnyAsync(s => s.Id == command.ServerId, cancellationToken))
                throw new ServerNotFoundException(command.ServerId);

            var sourceName = command.SourceKind == ImportSourceKind.Url ? command.Url : command.SourceName;

            if (string.IsNullOrWhiteSpace(sourceName))
                throw new InvalidRequestException("Upload a pack file or give a URL to import from.");

            if (command.SourceKind == ImportSourceKind.Url)
            {
                if (!Uri.TryCreate(sourceName, UriKind.Absolute, out var uri)
                    || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidRequestException("A pack URL must be an absolute https:// URL.");
                }

                if (!PackDownloadHosts.Allowed(configuration).Contains(uri.Host))
                {
                    throw new InvalidRequestException(
                        $"Download host not allowed: {uri.Host}. Add it to Hopper:PackDownloadHosts or upload the pack instead.");
                }
            }

            if (command.SourceKind == ImportSourceKind.Upload && command.Content is null)
                throw new InvalidRequestException("No pack file was uploaded.");

            var import = new ModImport
            {
                ServerId = command.ServerId,
                SourceName = sourceName,
                SourceKind = command.SourceKind,
                Format = PackFormat.Unknown,
                Status = ImportStatus.Queued,
                CreatedBy = currentUser.Name,
            };

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

        private long MaxImportBytes => HopperLimits.MaxImportBytes(configuration);
    }
}
