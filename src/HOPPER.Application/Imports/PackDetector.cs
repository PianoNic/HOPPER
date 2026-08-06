using System.IO.Compression;
using HOPPER.Domain.Enums;

namespace HOPPER.Application.Imports
{
    /// <summary>Decides what kind of archive the admin uploaded, in Prism Launcher's own order and
    /// with Prism's own asymmetry - which is worth copying rather than tidying up.
    ///
    /// Rules 1–3 match the FULL path, so only a root entry counts. That is deliberate: manifest.json
    /// is an ordinary filename and turns up inside overrides/ in real packs, so a basename match there
    /// would read a CurseForge pack out of a Modrinth one. Rule 4 matches the BASENAME anywhere,
    /// because a Prism export is legitimately zipped one directory deep by whoever shared it.</summary>
    public static class PackDetector
    {
        public static PackDetection Detect(ZipArchive archive)
        {
            if (DetectAt(archive, string.Empty) is { } atRoot)
                return atRoot;

            // A Prism instance may itself be a pack the user downloaded and never installed, so after
            // stripping the prefix the first three rules run again against the stripped tree.
            var instanceCfg = archive.Entries
                .FirstOrDefault(e => e.Name.Equals("instance.cfg", StringComparison.OrdinalIgnoreCase));

            if (instanceCfg is not null)
            {
                var prefix = DirectoryOf(instanceCfg.FullName);
                return DetectAt(archive, prefix) ?? new PackDetection(PackFormat.PrismInstance, prefix);
            }

            // Not a modpack at all, but a plain zip of jars is the multi-upload path and a perfectly
            // good thing to import.
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
                // Recognised on purpose so the admin gets a straight answer instead of "not a
                // recognised modpack", which would read as a corrupt download.
                throw new PackImportException("Technic packs are not supported.");
            }

            if (archive.GetEntry(prefix + "manifest.json") is not null)
                return new PackDetection(PackFormat.CurseForge, prefix);

            return null;
        }

        internal static bool IsJar(ZipArchiveEntry entry) =>
            entry.Name.Length > 0                                                   // a directory entry has an empty Name
            && entry.Name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase)
            && !entry.FullName.StartsWith("__MACOSX/", StringComparison.Ordinal);   // macOS resource forks, never real jars

        /// <summary>"MyPack/instance.cfg" -> "MyPack/", "instance.cfg" -> "". Zip paths always use
        /// forward slashes, so Path.GetDirectoryName is the wrong tool here.</summary>
        private static string DirectoryOf(string fullName)
        {
            var slash = fullName.LastIndexOf('/');
            return slash < 0 ? string.Empty : fullName[..(slash + 1)];
        }
    }
}
