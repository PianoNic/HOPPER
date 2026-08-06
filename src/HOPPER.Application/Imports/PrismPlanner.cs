using System.IO.Compression;
using HOPPER.Domain.Enums;

namespace HOPPER.Application.Imports
{
    public static class PrismPlanner
    {
        public static PackPlan Plan(ZipArchive archive, string prefix)
        {
            var root = $"{prefix}minecraft/mods/";
            if (!archive.Entries.Any(e => e.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
                root = $"{prefix}.minecraft/mods/";

            var files = archive.Entries
                .Where(e => e.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase) && PackDetector.IsJar(e))
                .Select(e => new PlannedFile { FileName = e.Name, ZipEntry = e.FullName })
                .ToList();

            if (files.Count == 0)
                throw new PackImportException("This Prism instance has no jars in its mods folder.");

            return new PackPlan { Format = PackFormat.PrismInstance, Files = files };
        }
    }

    public static class JarArchivePlanner
    {
        public static PackPlan Plan(ZipArchive archive)
        {
            var files = archive.Entries
                .Where(PackDetector.IsJar)
                .Select(e => new PlannedFile { FileName = e.Name, ZipEntry = e.FullName })
                .ToList();

            return new PackPlan { Format = PackFormat.JarArchive, Files = files };
        }
    }
}
