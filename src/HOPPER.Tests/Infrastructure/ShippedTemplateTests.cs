using System.Buffers.Binary;
using System.IO.Compression;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure.Services;

namespace HOPPER.Tests.Infrastructure
{
    /// The templates the endpoint actually serves, not one built by a fixture. Only
    /// hopper-forge-modern has ever been loaded by a real loader (#194); these are the invariants a
    /// test can hold for the other six without one.
    public class ShippedTemplateTests
    {
        // Java class file major versions. --release is what enforces each adapter's version floor,
        // and the floors were read out of real loader artifacts - see docs/locator.md.
        private const int Java8 = 52;
        private const int Java16 = 60;
        private const int Java17 = 61;
        private const int Java21 = 65;

        public static IEnumerable<Func<(string Jar, string Marker, int MaxMajor)>> Shipped()
        {
            yield return () => ("hopper-forge-1122.jar", "ch/pianonic/hopper/HopperCoreMod.class", Java8);
            yield return () => ("hopper-forge-1165.jar", ForgeService, Java8);
            yield return () => ("hopper-forge-1182.jar", ForgeService, Java16);
            yield return () => ("hopper-forge-modern.jar", ForgeService, Java17);
            yield return () => ("hopper-neoforge.jar", NeoForgeService, Java21);
            yield return () => ("hopper-fabric.jar", "fabric.mod.json", Java8);
            yield return () => ("hopper-quilt-plugin.jar", "quilt.mod.json", Java8);
        }

        private const string ForgeService = "META-INF/services/net.minecraftforge.forgespi.locating.IModLocator";
        private const string NeoForgeService = "META-INF/services/net.neoforged.neoforgespi.locating.IModFileCandidateLocator";

        /// Absent unless `cd src/HOPPER.Locator && ./gradlew templates` has been run, and the suite
        /// must not start needing gradle. The Docker build always has them.
        private static string? Directory()
        {
            var path = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "HOPPER.Locator", "build", "templates"));

            return System.IO.Directory.Exists(path) ? path : null;
        }

        [Test]
        [MethodDataSource(nameof(Shipped))]
        public async Task EveryTemplateIsAZipCarryingTheMarkerItIsAddressedBy(
            (string Jar, string Marker, int MaxMajor) shipped)
        {
            if (Directory() is not { } directory)
                return;

            var path = Path.Combine(directory, shipped.Jar);

            await Assert.That(File.Exists(path)).IsTrue();

            using var archive = ZipFile.OpenRead(path);

            await Assert.That(archive.GetEntry(shipped.Marker)).IsNotNull();
        }

        [Test]
        [MethodDataSource(nameof(Shipped))]
        public async Task NoClassIsNewerThanTheLoaderTheAdapterClaims(
            (string Jar, string Marker, int MaxMajor) shipped)
        {
            if (Directory() is not { } directory)
                return;

            using var archive = ZipFile.OpenRead(Path.Combine(directory, shipped.Jar));

            var classes = archive.Entries.Where(e => e.FullName.EndsWith(".class", StringComparison.Ordinal)).ToList();

            await Assert.That(classes).IsNotEmpty();

            foreach (var entry in classes)
            {
                var major = MajorVersionOf(entry);

                await Assert.That(major)
                    .IsLessThanOrEqualTo(shipped.MaxMajor)
                    .Because($"{shipped.Jar}!{entry.FullName} is class file major {major}, which the "
                             + "oldest loader in this adapter's range cannot load");
            }
        }

        [Test]
        public async Task EveryTemplateLocatorTemplatesCanNameIsOnDisk()
        {
            if (Directory() is not { } directory)
                return;

            // Every arm of LocatorTemplates.For, so a loader wired into the switch without a jar in
            // the templates task fails here rather than as a 503 the first time someone downloads it.
            var named = new[]
            {
                LocatorTemplates.For(ModLoader.Forge, "1.12.2"),
                LocatorTemplates.For(ModLoader.Forge, "1.16.5"),
                LocatorTemplates.For(ModLoader.Forge, "1.18.2"),
                LocatorTemplates.For(ModLoader.Forge, "1.20.1"),
                LocatorTemplates.For(ModLoader.NeoForge, "1.21.1"),
                LocatorTemplates.For(ModLoader.Fabric, "1.20.1"),
                LocatorTemplates.For(ModLoader.Quilt, "1.20.1"),
            };

            foreach (var template in named)
                await Assert.That(File.Exists(Path.Combine(directory, template.FileName))).IsTrue();
        }

        private static int MajorVersionOf(ZipArchiveEntry entry)
        {
            using var stream = entry.Open();

            Span<byte> header = stackalloc byte[8];
            stream.ReadExactly(header);

            return BinaryPrimitives.ReadUInt16BigEndian(header[6..8]);
        }
    }
}
