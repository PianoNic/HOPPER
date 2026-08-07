using System.IO.Compression;
using System.Text.Json;
using HOPPER.Application.Exports;
using HOPPER.Domain.Enums;

namespace HOPPER.Application.Imports
{
    public static class PrismPlanner
    {
        public static PackPlan Plan(ZipArchive archive, string prefix, PackPlanContext context)
        {
            var warnings = PackPlatformCheck.Verify(
                DeclaredPlatform(archive, prefix, context.MaxMetadataBytes), context.Target);

            var root = $"{prefix}minecraft/mods/";
            if (!archive.Entries.Any(e => e.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
                root = $"{prefix}.minecraft/mods/";

            var files = archive.Entries
                .Where(e => e.FullName.StartsWith(root, StringComparison.OrdinalIgnoreCase) && PackDetector.IsJar(e))
                .Select(e => new PlannedFile { FileName = e.Name, ZipEntry = e.FullName })
                .ToList();

            if (files.Count == 0)
                throw new PackImportException("This Prism instance has no jars in its mods folder.");

            return new PackPlan { Format = PackFormat.PrismInstance, Files = files, Warnings = warnings };
        }

        private static PackPlatform DeclaredPlatform(ZipArchive archive, string prefix, long maxBytes)
        {
            var text = ZipEntryText.Read(archive, prefix + "mmc-pack.json", maxBytes);
            if (text is null)
                return PackPlatform.Unknown;

            try
            {
                using var document = JsonDocument.Parse(text);

                if (!document.RootElement.TryGetProperty("components", out var components)
                    || components.ValueKind != JsonValueKind.Array)
                {
                    return PackPlatform.Unknown;
                }

                string? minecraft = null;
                var loader = ModLoader.Unknown;

                foreach (var component in components.EnumerateArray())
                {
                    if (component.ValueKind != JsonValueKind.Object
                        || !component.TryGetProperty("uid", out var uid)
                        || uid.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var version = component.TryGetProperty("version", out var v) && v.ValueKind == JsonValueKind.String
                        ? v.GetString()
                        : null;

                    if (string.Equals(uid.GetString(), LoaderIds.MinecraftUid, StringComparison.Ordinal))
                    {
                        minecraft = version;
                        continue;
                    }

                    var candidate = LoaderIds.FromPrismUid(uid.GetString());
                    if (candidate != ModLoader.Unknown)
                        loader = candidate;
                }

                return new PackPlatform(minecraft, loader);
            }
            catch (JsonException)
            {
                return PackPlatform.Unknown;
            }
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
