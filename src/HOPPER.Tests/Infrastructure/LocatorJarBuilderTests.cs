using System.IO.Compression;
using System.Text;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Tests.Infrastructure
{
    public class LocatorJarBuilderTests
    {
        private const string ServiceEntry = "META-INF/services/net.minecraftforge.forgespi.locating.IModLocator";

        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hopper-jar-" + Guid.NewGuid().ToString("N"));
            public TempDir() => Directory.CreateDirectory(Path);
            public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
        }

        private const string ForgeModernJar = "hopper-forge-modern.jar";

        private static string WriteTemplate(string directory, string name = ForgeModernJar)
        {
            var path = Path.Combine(directory, name);

            using (var file = File.Create(path))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            {
                Write(archive, "META-INF/MANIFEST.MF", "Manifest-Version: 1.0\nAutomatic-Module-Name: hopper\n");
                Write(archive, ServiceEntry, "ch.pianonic.hopper.HopperLocator\n");
                Write(archive, "ch/pianonic/hopper/HopperLocator.class", "Êþº¾ pretend bytecode");
            }

            return path;

            static void Write(ZipArchive archive, string entryName, string content)
            {
                using var stream = archive.CreateEntry(entryName).Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }

        private const string JarToolProducedJarBase64 =
            "UEsDBAoAAAgAAAWaBV0AAAAAAAAAAAAAAAAJAAQATUVUQS1JTkYv/soAAFBLAwQUAAgICAAFmgVdAAAAAAAAAAAAAAAAFAAAAE1F" +
            "VEEtSU5GL01BTklGRVNULk1G803My0xLLS7RDUstKs7Mz7NSMNQz4OVyLkpNLElN0XWqtFIwMtIz0DNU0PAvSkzOSVVwzi8qyC9K" +
            "LAEq1uTl4uUCAFBLBwi3KrY8QwAAAEIAAABQSwMECgAACAAA9JkFXQAAAAAAAAAAAAAAABIAAABNRVRBLUlORi9zZXJ2aWNlcy9Q" +
            "SwMEFAAICAgA9JkFXQAAAAAAAAAAAAAAAEIAAABNRVRBLUlORi9zZXJ2aWNlcy9uZXQubWluZWNyYWZ0Zm9yZ2UuZm9yZ2VzcGku" +
            "bG9jYXRpbmcuSU1vZExvY2F0b3JLztAryEzMy8/LTNbLyC8oSC3S8wBTPvnJiSX5RVwAUEsHCOVqs4ofAAAAIQAAAFBLAwQKAAAI" +
            "AAD0mQVdAAAAAAAAAAAAAAAAAwAAAGNoL1BLAwQKAAAIAAD0mQVdAAAAAAAAAAAAAAAADAAAAGNoL3BpYW5vbmljL1BLAwQKAAAI" +
            "AAD7mQVdAAAAAAAAAAAAAAAAEwAAAGNoL3BpYW5vbmljL2hvcHBlci9QSwMEFAAICAgA+5kFXQAAAAAAAAAAAAAAACYAAABjaC9w" +
            "aWFub25pYy9ob3BwZXIvSG9wcGVyTG9jYXRvci5jbGFzc2VOwWrCQBB9k2iSplpFvInQ3rQH9wMsPSiUHkI9KN4328Ws2N2wRL+r" +
            "PRV66Af0o4qTUCilc3hv5vFm5n19f3wCWKCfIkAYo9VBGxGhv5cnKQ7S7sQq32tVEaI7Y011Twgn022ChBAUJkZKuFaFKI20zhol" +
            "CleW2ovHhjKnZOU8obV0z5rQy4zVT8eXXPuNzA+sDCfT7PfXuvLG7uaEdO2OXukHU3sGf47NajtucMGJ6wpAdWbGS57GzMTcvn0H" +
            "vXFD6DBGjRjwUoLuj3XUaEA4iF//GQlXDffOUEsHCAQV9CHaAAAAIwEAAFBLAQIKAAoAAAgAAAWaBV0AAAAAAAAAAAAAAAAJAAQA" +
            "AAAAAAAAAAAAAAAAAABNRVRBLUlORi/+ygAAUEsBAhQAFAAICAgABZoFXbcqtjxDAAAAQgAAABQAAAAAAAAAAAAAAAAAKwAAAE1F" +
            "VEEtSU5GL01BTklGRVNULk1GUEsBAgoACgAACAAA9JkFXQAAAAAAAAAAAAAAABIAAAAAAAAAAAAAAAAAsAAAAE1FVEEtSU5GL3Nl" +
            "cnZpY2VzL1BLAQIUABQACAgIAPSZBV3larOKHwAAACEAAABCAAAAAAAAAAAAAAAAAOAAAABNRVRBLUlORi9zZXJ2aWNlcy9uZXQu" +
            "bWluZWNyYWZ0Zm9yZ2UuZm9yZ2VzcGkubG9jYXRpbmcuSU1vZExvY2F0b3JQSwECCgAKAAAIAAD0mQVdAAAAAAAAAAAAAAAAAwAA" +
            "AAAAAAAAAAAAAABvAQAAY2gvUEsBAgoACgAACAAA9JkFXQAAAAAAAAAAAAAAAAwAAAAAAAAAAAAAAAAAkAEAAGNoL3BpYW5vbmlj" +
            "L1BLAQIKAAoAAAgAAPuZBV0AAAAAAAAAAAAAAAATAAAAAAAAAAAAAAAAALoBAABjaC9waWFub25pYy9ob3BwZXIvUEsBAhQAFAAI" +
            "CAgA+5kFXQQV9CHaAAAAIwEAACYAAAAAAAAAAAAAAAAA6wEAAGNoL3BpYW5vbmljL2hvcHBlci9Ib3BwZXJMb2NhdG9yLmNsYXNz" +
            "UEsFBgAAAAAIAAgALQIAABkDAAAAAA==";

        private static string WriteTemplateFromTheJdkJarTool(string directory)
        {
            var path = Path.Combine(directory, ForgeModernJar);
            File.WriteAllBytes(path, Convert.FromBase64String(JarToolProducedJarBase64));
            return path;
        }

        private static List<string> EntriesWithBrokenLocalHeaders(byte[] jar)
        {
            var broken = new List<string>();

            var eocd = LastIndexOf(jar, [0x50, 0x4B, 0x05, 0x06]);
            var count = BitConverter.ToUInt16(jar, eocd + 10);
            var cursor = (int)BitConverter.ToUInt32(jar, eocd + 16);

            for (var i = 0; i < count; i++)
            {
                var nameLength = BitConverter.ToUInt16(jar, cursor + 28);
                var extraLength = BitConverter.ToUInt16(jar, cursor + 30);
                var commentLength = BitConverter.ToUInt16(jar, cursor + 32);
                var localOffset = (int)BitConverter.ToUInt32(jar, cursor + 42);
                var name = Encoding.UTF8.GetString(jar, cursor + 46, nameLength);

                if (jar[localOffset] != 0x50 || jar[localOffset + 1] != 0x4B
                    || jar[localOffset + 2] != 0x03 || jar[localOffset + 3] != 0x04)
                {
                    broken.Add(name);
                }

                cursor += 46 + nameLength + extraLength + commentLength;
            }

            return broken;

            static int LastIndexOf(byte[] haystack, byte[] needle)
            {
                for (var i = haystack.Length - needle.Length; i >= 0; i--)
                {
                    var match = true;
                    for (var j = 0; j < needle.Length && match; j++)
                        match = haystack[i + j] == needle[j];
                    if (match) return i;
                }

                throw new InvalidOperationException("No end-of-central-directory record.");
            }
        }

        private static LocatorJarBuilder BuilderFor(string? templateDirectory) =>
            new(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Hopper:LocatorTemplateDirectory"] = templateDirectory })
                .Build());

        private static byte[] BuildModernForge(LocatorJarBuilder builder, Guid serverId, string manifestUrl, string token) =>
            builder.Build(serverId, manifestUrl, token, ModLoader.Forge, "1.20.1");

        private static Dictionary<string, string> ReadProperties(byte[] jar)
        {
            using var archive = new ZipArchive(new MemoryStream(jar), ZipArchiveMode.Read);
            var entry = archive.GetEntry(LocatorJarBuilder.ConfigEntry)
                ?? throw new InvalidOperationException("hopper-server.properties is missing.");

            using var reader = new StreamReader(entry.Open());
            var values = new Dictionary<string, string>(StringComparer.Ordinal);

            while (reader.ReadLine() is { } line)
            {
                if (line.Length == 0 || line.StartsWith('#')) continue;
                var split = line.IndexOf('=');
                values[line[..split]] = line[(split + 1)..];
            }

            return values;
        }

        [Test]
        public async Task Build_Output_IsStillAReadableZip()
        {
            using var dir = new TempDir();
            WriteTemplate(dir.Path);
            var jar = BuildModernForge(BuilderFor(dir.Path), Guid.NewGuid(), "https://hopper.example.com/api/manifest", "t");

            using var archive = new ZipArchive(new MemoryStream(jar), ZipArchiveMode.Read);

            await Assert.That(archive.Entries.Select(e => e.FullName)).Contains(ServiceEntry);
            await Assert.That(archive.Entries.Select(e => e.FullName)).Contains("META-INF/MANIFEST.MF");
            await Assert.That(archive.Entries.Select(e => e.FullName)).Contains(LocatorJarBuilder.ConfigEntry);
        }

        [Test]
        public async Task Build_TemplateWrittenByTheJdkJarTool_KeepsEveryLocalHeaderOffsetValid()
        {
            using var dir = new TempDir();
            var template = WriteTemplateFromTheJdkJarTool(dir.Path);

            var templateBytes = await File.ReadAllBytesAsync(template);
            await Assert.That(HasDataDescriptorEntries(templateBytes))
                .IsTrue()
                .Because("the fixture must reproduce the shape the JDK jar tool writes");

            var jar = BuildModernForge(BuilderFor(dir.Path), Guid.NewGuid(), "https://hopper.example.com/api/manifest", "t");

            await Assert.That(EntriesWithBrokenLocalHeaders(jar)).IsEmpty();
        }

        private static bool HasDataDescriptorEntries(byte[] jar)
        {
            for (var i = 0; i < jar.Length - 8; i++)
            {
                if (jar[i] == 0x50 && jar[i + 1] == 0x4B && jar[i + 2] == 0x03 && jar[i + 3] == 0x04
                    && (BitConverter.ToUInt16(jar, i + 6) & 0x08) != 0)
                {
                    return true;
                }
            }

            return false;
        }

        [Test]
        public async Task Build_Output_HasNoEntryWhoseLocalHeaderOffsetIsWrong()
        {
            using var dir = new TempDir();
            WriteTemplate(dir.Path);
            var jar = BuildModernForge(BuilderFor(dir.Path), Guid.NewGuid(), "https://h/api/manifest", "t");

            await Assert.That(EntriesWithBrokenLocalHeaders(jar)).IsEmpty();
        }

        [Test]
        public async Task Build_ConfigEntry_CarriesExactlyTheThreeGeneratedKeys()
        {
            using var dir = new TempDir();
            var serverId = Guid.NewGuid();
            WriteTemplate(dir.Path);
            var jar = BuildModernForge(BuilderFor(dir.Path), serverId, "https://hopper.example.com/api/manifest", "0123456789abcdef");

            var properties = ReadProperties(jar);

            await Assert.That(properties.Keys.Order().ToList())
                .IsEquivalentTo(new[] { "manifestUrl", "serverId", "token" });
            await Assert.That(properties["serverId"]).IsEqualTo(serverId.ToString());
            await Assert.That(properties["manifestUrl"]).IsEqualTo("https://hopper.example.com/api/manifest");
            await Assert.That(properties["token"]).IsEqualTo("0123456789abcdef");
        }

        [Test]
        public async Task Build_ConfigEntry_IsAtTheArchiveRootWithNoLeadingSlash()
        {
            using var dir = new TempDir();
            WriteTemplate(dir.Path);
            var jar = BuildModernForge(BuilderFor(dir.Path), Guid.NewGuid(), "https://h/api/manifest", "t");

            using var archive = new ZipArchive(new MemoryStream(jar), ZipArchiveMode.Read);
            var entry = archive.Entries.Single(e => e.Name == "hopper-server.properties");

            await Assert.That(entry.FullName).IsEqualTo("hopper-server.properties");
        }

        [Test]
        public async Task Build_TemplateEntries_AreLeftByteIdentical()
        {
            using var dir = new TempDir();
            var template = WriteTemplate(dir.Path);
            var jar = BuildModernForge(BuilderFor(dir.Path), Guid.NewGuid(), "https://h/api/manifest", "t");

            using var before = ZipFile.OpenRead(template);
            using var after = new ZipArchive(new MemoryStream(jar), ZipArchiveMode.Read);

            foreach (var original in before.Entries)
            {
                var patched = after.GetEntry(original.FullName);
                await Assert.That(patched).IsNotNull();
                await Assert.That(Bytes(patched!)).IsEquivalentTo(Bytes(original));
            }

            static byte[] Bytes(ZipArchiveEntry entry)
            {
                using var stream = entry.Open();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return buffer.ToArray();
            }
        }

        [Test]
        public async Task Build_Twice_ReplacesTheConfigEntryRatherThanAppendingASecond()
        {
            using var dir = new TempDir();
            var template = WriteTemplate(dir.Path);
            var builder = BuilderFor(dir.Path);

            var first = BuildModernForge(builder, Guid.NewGuid(), "https://first/api/manifest", "first-token");
            File.WriteAllBytes(template, first);
            var second = BuildModernForge(builder, Guid.NewGuid(), "https://second/api/manifest", "second-token");

            using var archive = new ZipArchive(new MemoryStream(second), ZipArchiveMode.Read);

            await Assert.That(archive.Entries.Count(e => e.FullName == LocatorJarBuilder.ConfigEntry)).IsEqualTo(1);
            await Assert.That(ReadProperties(second)["token"]).IsEqualTo("second-token");
        }

        [Test]
        public async Task Build_Template_IsNeverModified()
        {
            using var dir = new TempDir();
            var template = WriteTemplate(dir.Path);
            var before = await File.ReadAllBytesAsync(template);

            BuildModernForge(BuilderFor(dir.Path), Guid.NewGuid(), "https://h/api/manifest", "t");

            await Assert.That(await File.ReadAllBytesAsync(template)).IsEquivalentTo(before);
        }

        [Test]
        public async Task Build_MissingTemplate_ThrowsAndNamesThePathAndTheKey()
        {
            using var dir = new TempDir();

            var exception = await Assert.That(() => BuildModernForge(BuilderFor(dir.Path), Guid.NewGuid(), "https://h/api/manifest", "t"))
                .Throws<LocatorTemplateMissingException>();

            await Assert.That(exception!.Message).Contains(ForgeModernJar);
            await Assert.That(exception.Message).Contains("Hopper:LocatorTemplateDirectory");
        }

        [Test]
        public async Task Build_TemplateThatIsNotAZip_ThrowsRatherThanServingRubbish()
        {
            using var dir = new TempDir();
            await File.WriteAllTextAsync(Path.Combine(dir.Path, ForgeModernJar), "this is not a zip");

            await Assert.That(() => BuildModernForge(BuilderFor(dir.Path), Guid.NewGuid(), "https://h/api/manifest", "t"))
                .Throws<LocatorTemplateMissingException>();
        }

        [Test]
        public async Task Build_ZipThatIsNotALocatorJar_ThrowsRatherThanShippingAJarThatDoesNothing()
        {
            using var dir = new TempDir();

            using (var file = File.Create(Path.Combine(dir.Path, ForgeModernJar)))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            using (var stream = archive.CreateEntry("META-INF/MANIFEST.MF").Open())
            {
                stream.Write("Manifest-Version: 1.0\n"u8);
            }

            await Assert.That(() => BuildModernForge(BuilderFor(dir.Path), Guid.NewGuid(), "https://h/api/manifest", "t"))
                .Throws<LocatorTemplateMissingException>();
        }

        [Test]
        [Arguments(ModLoader.Forge, "1.12.2", "hopper-forge-1122.jar")]
        [Arguments(ModLoader.Forge, "1.16.5", "hopper-forge-1165.jar")]
        [Arguments(ModLoader.Forge, "1.14.4", "hopper-forge-1165.jar")]
        [Arguments(ModLoader.Forge, "1.18.2", "hopper-forge-1182.jar")]
        [Arguments(ModLoader.Forge, "1.17.1", "hopper-forge-1182.jar")]
        [Arguments(ModLoader.Forge, "1.19.2", "hopper-forge-modern.jar")]
        [Arguments(ModLoader.Forge, "1.21.4", "hopper-forge-modern.jar")]
        [Arguments(ModLoader.NeoForge, "1.21.1", "hopper-neoforge.jar")]
        [Arguments(ModLoader.Fabric, "1.21.1", "hopper-fabric.jar")]
        public async Task For_LoaderAndVersion_PicksTheAdapterThatLoaderCanActuallyRead(
            ModLoader loader, string minecraftVersion, string expected)
        {
            await Assert.That(LocatorTemplates.For(loader, minecraftVersion).FileName).IsEqualTo(expected);
        }

        [Test]
        public async Task For_Quilt_IsServedTheFabricJar()
        {
            await Assert.That(LocatorTemplates.For(ModLoader.Quilt, "1.21.1").FileName).IsEqualTo("hopper-fabric.jar");
        }

        [Test]
        [Arguments("25w14a")]
        [Arguments("23w13a_or_b")]
        [Arguments("")]
        [Arguments(null)]
        public async Task For_AVersionThatIsNotAPlainRelease_FallsForwardToModern(string? minecraftVersion)
        {
            await Assert.That(LocatorTemplates.For(ModLoader.Forge, minecraftVersion).FileName)
                .IsEqualTo("hopper-forge-modern.jar");
        }

        [Test]
        public async Task For_UnknownLoader_ThrowsRatherThanGuessing()
        {
            await Assert.That(() => LocatorTemplates.For(ModLoader.Unknown, "1.20.1"))
                .Throws<LocatorLoaderNotConfiguredException>();
        }

        [Test]
        public async Task Build_UnknownLoader_ThrowsBeforeItLooksForAFile()
        {
            await Assert.That(() => BuilderFor("/no/such/directory")
                    .Build(Guid.NewGuid(), "https://h/api/manifest", "t", ModLoader.Unknown, "1.20.1"))
                .Throws<LocatorLoaderNotConfiguredException>();
        }

        [Test]
        public async Task Build_MarkerCheck_IsPerLoaderRatherThanAlwaysForge()
        {
            using var dir = new TempDir();

            using (var file = File.Create(Path.Combine(dir.Path, "hopper-fabric.jar")))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            using (var stream = archive.CreateEntry("fabric.mod.json").Open())
            {
                stream.Write("""{"schemaVersion":1,"id":"hopper"}"""u8);
            }

            var jar = BuilderFor(dir.Path).Build(Guid.NewGuid(), "https://h/api/manifest", "tok", ModLoader.Fabric, "1.21.1");

            await Assert.That(ReadProperties(jar)["token"]).IsEqualTo("tok");
        }

        [Test]
        public async Task Build_AdapterForAnotherLoader_IsRejected()
        {
            using var dir = new TempDir();
            WriteTemplate(dir.Path, "hopper-fabric.jar");

            await Assert.That(() => BuilderFor(dir.Path)
                    .Build(Guid.NewGuid(), "https://h/api/manifest", "t", ModLoader.Fabric, "1.21.1"))
                .Throws<LocatorTemplateMissingException>();
        }
    }
}
