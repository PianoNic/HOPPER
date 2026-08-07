using HOPPER.Infrastructure.Interfaces;

namespace HOPPER.Application.ModMetadata
{
    public static class ModIconStore
    {
        public static async Task<string?> FromStagedJarAsync(
            IBlobStorage blobs, StagedBlob staged, CancellationToken cancellationToken)
        {
            byte[]? icon;
            try
            {
                using var jar = blobs.OpenStaged(staged);
                icon = ModIconReader.Read(jar);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                return null;
            }

            return icon is null ? null : await StoreAsync(blobs, icon, cancellationToken);
        }

        public static async Task<string?> FromJarAsync(
            IBlobStorage blobs, string sha256, CancellationToken cancellationToken)
        {
            byte[]? icon;
            try
            {
                using var jar = blobs.OpenRead(sha256);
                if (jar is null) return null;

                using var seekable = new MemoryStream();
                await jar.CopyToAsync(seekable, cancellationToken);
                seekable.Position = 0;

                icon = ModIconReader.Read(seekable);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                return null;
            }

            return icon is null ? null : await StoreAsync(blobs, icon, cancellationToken);
        }

        public static async Task<string?> StoreAsync(
            IBlobStorage blobs, byte[] icon, CancellationToken cancellationToken)
        {
            try
            {
                using var source = new MemoryStream(icon);
                var staged = await blobs.StageAsync(source, ModIconReader.MaxIconBytes, cancellationToken);

                blobs.Promote(staged);
                return staged.Sha256;
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
