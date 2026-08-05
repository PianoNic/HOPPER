using HOPPER.Application.Dtos.Mods;
using HOPPER.Application.Mappings.Mods;
using HOPPER.Domain;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Mods
{
    /// <summary>The jar arrives as a Stream rather than a byte[] so it is written and hashed straight
    /// from the request body — a large content mod never has to be buffered by us.</summary>
    public record UploadModCommand(string FileName, Stream Content) : ICommand<ModDto>;

    public class UploadModCommandHandler(HopperDbContext db, IBlobStorage blobs, ICurrentUserService currentUser)
        : ICommandHandler<UploadModCommand, ModDto>
    {
        public async ValueTask<ModDto> Handle(UploadModCommand command, CancellationToken cancellationToken)
        {
            var fileName = ModFileNameValidator.Validate(command.FileName);

            // Replacing a jar in place would hand every client a same-named file with a new hash and
            // leave no trace of what was swapped, so a changed jar is an explicit delete-then-upload.
            if (await db.Mods.AnyAsync(m => m.FileName == fileName, cancellationToken))
                throw new DuplicateModFileNameException(fileName);

            var (sha256, size) = await blobs.SaveAsync(command.Content, cancellationToken);

            var entry = new Mod
            {
                FileName = fileName,
                Sha256 = sha256,
                Size = size,
                UploadedBy = currentUser.Name,
            };

            db.Mods.Add(entry);
            await db.SaveChangesAsync(cancellationToken);

            return entry.ToDto();
        }
    }
}
