using Microsoft.Extensions.Configuration;

namespace HOPPER.Application.Imports
{
    public static class PackDownloadHosts
    {
        public static readonly string[] Defaults =
        [
            "cdn.modrinth.com",
            "github.com",
            "raw.githubusercontent.com",
            "gitlab.com",
            "edge.forgecdn.net",
            "mediafilez.forgecdn.net",
        ];

        public static HashSet<string> Allowed(IConfiguration configuration)
        {
            var configured = configuration.GetSection("Hopper:PackDownloadHosts").Get<string[]>();
            var hosts = configured is { Length: > 0 } ? configured : Defaults;

            return hosts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
    }
}
