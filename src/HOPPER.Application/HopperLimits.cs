using Microsoft.Extensions.Configuration;

namespace HOPPER.Application
{
    public static class HopperLimits
    {
        public const long DefaultMaxModBytes = 512L * 1024 * 1024;

        public const long DefaultMaxImportBytes = 2L * 1024 * 1024 * 1024;

        public const long DefaultMaxPackMetadataBytes = 8L * 1024 * 1024;

        public const int DefaultMaxReportedMods = 2000;

        public const int MaxFileNameLength = 255;

        public const int MaxClientIdLength = 200;

        public const int MaxUsernameLength = 100;

        public static long MaxModBytes(IConfiguration configuration) =>
            configuration.GetValue("Hopper:MaxModBytes", DefaultMaxModBytes);

        public static long MaxImportBytes(IConfiguration configuration) =>
            configuration.GetValue("Hopper:MaxImportBytes", DefaultMaxImportBytes);

        public static long MaxPackMetadataBytes(IConfiguration configuration) =>
            configuration.GetValue("Hopper:MaxPackMetadataBytes", DefaultMaxPackMetadataBytes);

        public static int MaxReportedMods(IConfiguration configuration) =>
            configuration.GetValue("Hopper:MaxReportedMods", DefaultMaxReportedMods);
    }
}
