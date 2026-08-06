using System.IO.Compression;
using HOPPER.Domain.Enums;

namespace HOPPER.Application.Imports
{
    public static class PackDetector
    {
        public static PackDetection Detect(ZipArchive archive)
        {
            if (DetectAt(archive, string.Empty) is { } atRoot)
                return atRoot;

            var instanceCfg = archive.Entries
                .FirstOrDefault(e => e.Name.Equals("instance.cfg", StringComparison.OrdinalIgnoreCase));

            if (instanceCfg is not null)
            {
                var prefix = DirectoryOf(instanceCfg.FullName);
                return DetectAt(archive, prefix) ?? new PackDetection(PackFormat.PrismInstance, prefix);
            }

            if (archive.Entries.Any(IsJar))
                return new PackDetection(PackFormat.JarArchive, string.Empty);

            throw new PackImportException(
                "Not a recognised modpack or jar archive. HOPPER reads .mrpack, CurseForge and Prism exports, and plain zips of jars.");
        }

        private static PackDetection? DetectAt(ZipArchive archive, string prefix)
        {
            if (archive.GetEntry(prefix + "modrinth.index.json") is not null)
                return new PackDetection(PackFormat.Modrinth, prefix);

            if (archive.GetEntry(prefix + "bin/modpack.jar") is not null
                || archive.GetEntry(prefix + "bin/version.json") is not null)
            {
                throw new PackImportException("Technic packs are not supported.");
            }

            if (archive.GetEntry(prefix + "manifest.json") is not null)
                return new PackDetection(PackFormat.CurseForge, prefix);

            return null;
        }

        internal static bool IsJar(ZipArchiveEntry entry) =>
            entry.Name.Length > 0
            && entry.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            && !entry.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal);

        private static string DirectoryOf(string fullName)
        {
            var slash = fullName.LastIndexOf('/');
            return slash < 0 ? string.Empty : fullName[..(slash + 1)];
        }
    }
}
