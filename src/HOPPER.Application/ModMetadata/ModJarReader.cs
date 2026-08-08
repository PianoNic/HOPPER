using System.IO.Compression;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure.Interfaces;

namespace HOPPER.Application.ModMetadata
{
    public readonly record struct ModJarMetadata(ModSide Side, string[]? ModIds, string? IconSha256, string[]? RequiredMods = null, string[]? BundledMods = null);

    public static class ModJarReader
    {
        public static async Task<ModJarMetadata> FromStagedAsync(
            IBlobStorage blobs, StagedBlob staged, CancellationToken cancellationToken)
        {
            Stream stream;

            try
            {
                stream = blobs.OpenStaged(staged);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                // Null ids rather than none: the jar was never read, so the backfill should come back to it.
                return new ModJarMetadata(ModSide.Both, null, null);
            }

            ModSide side;
            string[] modIds;
            string[] requiredMods;
            string[] bundledMods;
            byte[]? icon;

            using (stream)
            {
                ZipArchive archive;

                try
                {
                    archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
                }
                catch (Exception ex) when (ex is InvalidDataException
                                              or IOException
                                              or NotSupportedException
                                              or ArgumentException)
                {
                    // Opened but unreadable is an answer: no ids, rather than unknown ids.
                    return new ModJarMetadata(ModSide.Both, [], null);
                }

                using (archive)
                {
                    side = ModSideReader.Read(archive);
                    modIds = ModIdReader.Read(archive);
                    requiredMods = ModDependencyReader.Read(archive);
                    bundledMods = ModDependencyReader.BundledIn(archive);
                    icon = ModIconReader.Read(archive);
                }
            }

            return new ModJarMetadata(
                side,
                modIds,
                icon is null ? null : await ModIconStore.StoreAsync(blobs, icon, cancellationToken),
                requiredMods,
                bundledMods);
        }
    }
}
