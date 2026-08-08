using HOPPER.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;

namespace HOPPER.API.Extensions
{
    public static class DataProtectionExtensions
    {
        /// Nothing in HOPPER protects anything with these today - it authenticates with bearer tokens
        /// it does not issue - but the default key ring lives inside the container and warns loudly
        /// on every start that it will not survive being recreated. Putting it on the volume that
        /// already persists makes the warning go away and keeps it true if something starts using it.
        public static IServiceCollection AddHopperDataProtection(
            this IServiceCollection services, IConfiguration configuration)
        {
            var directory = new DirectoryInfo(KeyDirectory(configuration));
            directory.Create();

            services.AddDataProtection()
                .PersistKeysToFileSystem(directory)
                // Pinned so the keys are not isolated by content root path, which would silently
                // start a new ring the first time the app moves.
                .SetApplicationName("hopper");

            return services;
        }

        public static string KeyDirectory(IConfiguration configuration)
        {
            if (configuration["DataProtection:Directory"] is { Length: > 0 } configured)
                return configured;

            // Beside the blobs rather than inside them: the reclaim sweep owns everything under the
            // blob root, and a key ring is not a blob.
            var blobs = BlobPaths.Root(configuration);

            return Path.Combine(Path.GetDirectoryName(blobs.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                                ?? blobs, "keys");
        }
    }
}
