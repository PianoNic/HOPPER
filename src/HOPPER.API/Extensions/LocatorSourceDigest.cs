using System.Security.Cryptography;
using System.Text;

namespace HOPPER.API.Extensions
{
    /// Pairs with the templatesStamp task in src/HOPPER.Locator/build.gradle - see docs/locator.md.
    public static class LocatorSourceDigest
    {
        public const string StampFileName = "templates.stamp";

        public static string? Of(string sourceDirectory)
        {
            var files = Directory
                .EnumerateFiles(sourceDirectory, "*.java", SearchOption.AllDirectories)
                .Where(path => !Relative(sourceDirectory, path).StartsWith("build/", StringComparison.Ordinal)
                               && !Relative(sourceDirectory, path).Contains("/build/", StringComparison.Ordinal))
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToList();

            if (files.Count == 0)
                return null;

            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            foreach (var path in files)
            {
                digest.AppendData(Encoding.UTF8.GetBytes(Relative(sourceDirectory, path) + "\n"));
                digest.AppendData(File.ReadAllBytes(path));
            }

            return Convert.ToHexStringLower(digest.GetHashAndReset());
        }

        private static string Relative(string root, string path) =>
            Path.GetRelativePath(root, path).Replace('\\', '/');
    }
}
