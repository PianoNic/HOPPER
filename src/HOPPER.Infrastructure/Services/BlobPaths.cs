using Microsoft.Extensions.Configuration;

namespace HOPPER.Infrastructure.Services
{
    public static class BlobPaths
    {
        public const string ExportScratchExtension = ".tmp";

        public static string Root(IConfiguration configuration) =>
            configuration["Blobs:Directory"] is { Length: > 0 } configured
                ? configured
                : Path.Combine(AppContext.BaseDirectory, "blobs");

        public static string Imports(IConfiguration configuration) => Path.Combine(Root(configuration), "imports");

        public static string Exports(IConfiguration configuration) => Path.Combine(Root(configuration), "exports");

        public static string Staging(IConfiguration configuration) => Path.Combine(Root(configuration), "tmp");
    }
}
