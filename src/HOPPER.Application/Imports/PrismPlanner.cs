using System.IO.Compression;
using HOPPER.Domain.Enums;

namespace HOPPER.Application.Imports
{
    /// <summary>Plans a Prism/MultiMC instance export. The easiest of the three: an instance is a
    /// materialised game directory, so the jars are literal bytes in the zip and there is nothing to
    /// download, resolve or key.
    ///
    /// Only reached when the stripped tree is not itself an .mrpack or a CurseForge pack - the
    /// detector re-runs the first three rules after stripping, so a re-zipped-but-never-installed pack
    /// is delegated to its real planner rather than landing here with an empty mods folder.</summary>
    public static class PrismPlanner
    {
        public static PackPlan Plan(ZipArchive archive, string prefix)
        {
            // MinecraftInstance::gameRoot()'s own rule: prefer "minecraft/", fall back to ".minecraft/"
            // only when the dotted form is the one that exists. Older MultiMC instances use the dot.
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

    /// <summary>Plans a plain zip of jars. Every jar anywhere in the archive, taken by basename - the
    /// folder someone happened to zip them from is not information a client can use, since it puts
    /// everything flat in hoppermods/.</summary>
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
