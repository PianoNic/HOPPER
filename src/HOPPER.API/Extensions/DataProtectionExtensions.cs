using HOPPER.Infrastructure.Services;
using Microsoft.AspNetCore.DataProtection;

namespace HOPPER.API.Extensions
{
    public static class DataProtectionExtensions
    {
        /// Nothing protects anything with these today, but the default ring dies with the container
        /// and says so on every start. See docs/self-host.md.
        public static IServiceCollection AddHopperDataProtection(
            this IServiceCollection services, IConfiguration configuration)
        {
            var directory = new DirectoryInfo(KeyDirectory(configuration));
            directory.Create();

            services.AddDataProtection()
                .PersistKeysToFileSystem(directory)
                // Pinned, or moving the app silently starts a new ring.
                .SetApplicationName("hopper");

            return services;
        }

        public static string KeyDirectory(IConfiguration configuration)
        {
            if (configuration["DataProtection:Directory"] is { Length: > 0 } configured)
                return configured;

            // Beside the blobs, not inside: the reclaim sweep owns everything under that root.
            var blobs = BlobPaths.Root(configuration);

            return Path.Combine(Path.GetDirectoryName(blobs.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                                ?? blobs, "keys");
        }
    }
}
