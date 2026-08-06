using System.IO.Compression;
using System.Text;
using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Tests.Infrastructure
{
    /// <summary>
    /// The jar handed to a player is a zip HOPPER edited. If that edit produces something Java cannot
    /// open, Forge does not report a bad archive - it skips the file, the locator never runs, and the
    /// player sees a vanilla game with no error anywhere near the cause. So these tests re-open the
    /// output with the same zip reader and assert the archive survived, not just that bytes came back.
    /// </summary>
    public class LocatorJarBuilderTests
    {
        private const string ServiceEntry = "META-INF/services/net.minecraftforge.forgespi.locating.IModLocator";

        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hopper-jar-" + Guid.NewGuid().ToString("N"));
            public TempDir() => Directory.CreateDirectory(Path);
            public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
        }

        /// <summary>A stand-in for src/HOPPER.Locator/build/libs/hopper-1.0.0.jar: a real zip carrying the service
        /// registration the builder checks for, plus a class file and a manifest so there is something
        /// to prove the builder left alone.</summary>
        private static string WriteTemplate(string directory, string name = "hopper.jar")
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

        /// <summary>A real jar produced by the JDK's `jar` tool, embedded verbatim so this fixture is
        /// exactly the byte shape that broke rather than a reconstruction of it. Its compressed entries
        /// carry general-purpose flag 0x0808 with trailing data descriptors, and it mixes those with
        /// stored directory entries and an extra field - the combination .NET's in-place updater gets
        /// wrong. Contents: META-INF/MANIFEST.MF, the IModLocator service registration, and one class.
        ///
        /// Regenerate with: jar --create --file hopper-template.jar -C &lt;classes&gt; .
        /// </summary>
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
            var path = Path.Combine(directory, "jar-tool-produced.jar");
            File.WriteAllBytes(path, Convert.FromBase64String(JarToolProducedJarBase64));
            return path;
        }

        /// <summary>Walks the central directory and checks that every entry's recorded local-header
        /// offset really points at a PK\x03\x04 signature. That is precisely the check java.util.zip
        /// performs before it will read an entry, and precisely what a rewrite that miscounts data
        /// descriptors breaks. Returns the names of the entries whose offsets are wrong.</summary>
        private static List<string> EntriesWithBrokenLocalHeaders(byte[] jar)
        {
            var broken = new List<string>();

            // End of central directory: signature, then the count and the directory's own offset.
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

        private static LocatorJarBuilder BuilderFor(string? templatePath) =>
            new(new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Hopper:LocatorTemplatePath"] = templatePath })
                .Build());

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
            // The whole feature rests on this: a jar is a zip, and an edit that corrupts the central
            // directory produces a file Forge silently ignores rather than one it complains about.
            using var dir = new TempDir();
            var jar = BuilderFor(WriteTemplate(dir.Path)).Build(Guid.NewGuid(), "https://hopper.example.com/api/manifest", "t");

            using var archive = new ZipArchive(new MemoryStream(jar), ZipArchiveMode.Read);

            await Assert.That(archive.Entries.Select(e => e.FullName)).Contains(ServiceEntry);
            await Assert.That(archive.Entries.Select(e => e.FullName)).Contains("META-INF/MANIFEST.MF");
            await Assert.That(archive.Entries.Select(e => e.FullName)).Contains(LocatorJarBuilder.ConfigEntry);
        }

        [Test]
        public async Task Build_TemplateWrittenByTheJdkJarTool_KeepsEveryLocalHeaderOffsetValid()
        {
            // The regression this whole file exists for. ZipArchiveMode.Update on a jar the JDK's
            // `jar` tool produced drops the trailing data descriptors those entries carry but does not
            // subtract them from the offsets it writes into the central directory, so every entry
            // after the first compressed one is recorded 16 bytes past where it really is.
            //
            // .NET's own reader is lenient enough that a round-trip test would pass. java.util.zip is
            // not: it answers "invalid LOC header (bad signature)" for exactly the class files and the
            // service registration, and Forge responds by skipping the jar in silence. So this asserts
            // the byte-level invariant Java checks rather than anything .NET would tell us.
            using var dir = new TempDir();
            var template = WriteTemplateFromTheJdkJarTool(dir.Path);

            // Guard the fixture itself: if this stopped producing data descriptors the test would keep
            // passing while testing nothing.
            var templateBytes = await File.ReadAllBytesAsync(template);
            await Assert.That(HasDataDescriptorEntries(templateBytes))
                .IsTrue()
                .Because("the fixture must reproduce the shape the JDK jar tool writes");

            var jar = BuilderFor(template).Build(Guid.NewGuid(), "https://hopper.example.com/api/manifest", "t");

            await Assert.That(EntriesWithBrokenLocalHeaders(jar)).IsEmpty();
        }

        /// <summary>True when any local header has general-purpose flag bit 3 set - sizes deferred to
        /// a trailing data descriptor.</summary>
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
            var jar = BuilderFor(WriteTemplate(dir.Path)).Build(Guid.NewGuid(), "https://h/api/manifest", "t");

            await Assert.That(EntriesWithBrokenLocalHeaders(jar)).IsEmpty();
        }

        [Test]
        public async Task Build_ConfigEntry_CarriesExactlyTheThreeGeneratedKeys()
        {
            // Three keys and no more. `enabled` is deliberately absent: it stays the player's on-disk
            // kill switch, and a jar that set it would take that away.
            using var dir = new TempDir();
            var serverId = Guid.NewGuid();
            var jar = BuilderFor(WriteTemplate(dir.Path))
                .Build(serverId, "https://hopper.example.com/api/manifest", "0123456789abcdef");

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
            // The Java side reads it with getResourceAsStream("/hopper-server.properties"), which
            // resolves to a root entry named without the slash. Anywhere else and the jar configures
            // nothing while looking perfectly fine.
            using var dir = new TempDir();
            var jar = BuilderFor(WriteTemplate(dir.Path)).Build(Guid.NewGuid(), "https://h/api/manifest", "t");

            using var archive = new ZipArchive(new MemoryStream(jar), ZipArchiveMode.Read);
            var entry = archive.Entries.Single(e => e.Name == "hopper-server.properties");

            await Assert.That(entry.FullName).IsEqualTo("hopper-server.properties");
        }

        [Test]
        public async Task Build_TemplateEntries_AreLeftByteIdentical()
        {
            using var dir = new TempDir();
            var template = WriteTemplate(dir.Path);
            var jar = BuilderFor(template).Build(Guid.NewGuid(), "https://h/api/manifest", "t");

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
            // A zip may legally hold two entries with the same name, and Java's class loader hands out
            // whichever it meets first - which on a doubly patched jar is the stale one. That would be
            // a jar that authenticates against the previous token and looks fine on inspection.
            using var dir = new TempDir();
            var template = WriteTemplate(dir.Path);
            var builder = BuilderFor(template);

            var first = builder.Build(Guid.NewGuid(), "https://first/api/manifest", "first-token");
            File.WriteAllBytes(template, first);
            var second = builder.Build(Guid.NewGuid(), "https://second/api/manifest", "second-token");

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

            BuilderFor(template).Build(Guid.NewGuid(), "https://h/api/manifest", "t");

            await Assert.That(await File.ReadAllBytesAsync(template)).IsEquivalentTo(before);
        }

        [Test]
        public async Task Build_MissingTemplate_ThrowsAndNamesThePathAndTheKey()
        {
            using var dir = new TempDir();
            var missing = Path.Combine(dir.Path, "not-built-yet.jar");

            var exception = await Assert.That(() => BuilderFor(missing).Build(Guid.NewGuid(), "https://h/api/manifest", "t"))
                .Throws<LocatorTemplateMissingException>();

            // An admin who has not run the Gradle build needs to be told where HOPPER looked and which
            // key moves it, not "object reference not set".
            await Assert.That(exception!.Message).Contains("not-built-yet.jar");
            await Assert.That(exception.Message).Contains("Hopper:LocatorTemplatePath");
        }

        [Test]
        public async Task Build_TemplateThatIsNotAZip_ThrowsRatherThanServingRubbish()
        {
            using var dir = new TempDir();
            var path = Path.Combine(dir.Path, "hopper.jar");
            await File.WriteAllTextAsync(path, "this is not a zip");

            await Assert.That(() => BuilderFor(path).Build(Guid.NewGuid(), "https://h/api/manifest", "t"))
                .Throws<LocatorTemplateMissingException>();
        }

        [Test]
        public async Task Build_ZipThatIsNotALocatorJar_ThrowsRatherThanShippingAJarThatDoesNothing()
        {
            // Without the service registration Forge loads the jar and never calls into it, so the
            // player gets a mods folder that syncs nothing and no error at all.
            using var dir = new TempDir();
            var path = Path.Combine(dir.Path, "some-other.jar");

            using (var file = File.Create(path))
            using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
            using (var stream = archive.CreateEntry("META-INF/MANIFEST.MF").Open())
            {
                stream.Write("Manifest-Version: 1.0\n"u8);
            }

            await Assert.That(() => BuilderFor(path).Build(Guid.NewGuid(), "https://h/api/manifest", "t"))
                .Throws<LocatorTemplateMissingException>();
        }
    }
}
