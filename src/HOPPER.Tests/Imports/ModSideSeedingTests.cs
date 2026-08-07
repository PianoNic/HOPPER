using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HOPPER.Application.Imports;
using HOPPER.Application.ModMetadata;
using HOPPER.Domain.Enums;

namespace HOPPER.Tests.Imports
{
    /// <summary>
    /// Setting the side by hand for a 474-mod pack is not a feature, it is a punishment. Every
    /// format already carries the answer; these pin that HOPPER reads it.
    /// </summary>
    public class ModSideSeedingTests
    {
        private static ZipArchive ArchiveOf(params (string Path, string Content)[] entries)
        {
            var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (path, content) in entries)
                {
                    using var stream = archive.CreateEntry(path).Open();
                    stream.Write(Encoding.UTF8.GetBytes(content));
                }
            }

            buffer.Position = 0;
            return new ZipArchive(buffer, ZipArchiveMode.Read);
        }

        // ---- the shared vocabulary --------------------------------------------------------

        [Test]
        [Arguments("required", "required", ModSide.Both)]
        [Arguments("optional", "optional", ModSide.Both)]
        [Arguments("unsupported", "required", ModSide.ServerOnly)]
        [Arguments("unsupported", "optional", ModSide.ServerOnly)]
        [Arguments("required", "unsupported", ModSide.ClientOnly)]
        [Arguments("optional", "unsupported", ModSide.ClientOnly)]
        [Arguments(null, null, ModSide.Both)]
        [Arguments("nonsense", "nonsense", ModSide.Both)]
        public async Task Side_MapsTheModrinthVocabulary(string? client, string? server, ModSide expected)
        {
            await Assert.That(PackEnv.Side(client, server)).IsEqualTo(expected);
        }

        [Test]
        public async Task Side_UnsupportedOnBothSidesStaysBoth()
        {
            // A contradiction the pack has to answer for. Dropping the jar would hide it; Both
            // keeps it visible in the dashboard where the admin can see it and decide.
            await Assert.That(PackEnv.Side("unsupported", "unsupported")).IsEqualTo(ModSide.Both);
        }

        // ---- mrpack ------------------------------------------------------------------------

        [Test]
        public async Task Mrpack_EnvPerFile_BecomesTheSide()
        {
            using var archive = ArchiveOf(("modrinth.index.json", """
                {"formatVersion":1,"game":"minecraft","files":[
                  {"path":"mods/client-thing.jar","hashes":{"sha1":"a"},"env":{"client":"required","server":"unsupported"},
                   "downloads":["https://cdn.modrinth.com/a.jar"],"fileSize":1},
                  {"path":"mods/server-thing.jar","hashes":{"sha1":"b"},"env":{"client":"unsupported","server":"required"},
                   "downloads":["https://cdn.modrinth.com/b.jar"],"fileSize":1},
                  {"path":"mods/both-thing.jar","hashes":{"sha1":"c"},"env":{"client":"required","server":"required"},
                   "downloads":["https://cdn.modrinth.com/c.jar"],"fileSize":1}
                ]}
                """));

            var plan = ModrinthPlanner.Plan(archive, string.Empty, PackPlanContext.Default);
            var sides = plan.Files.ToDictionary(f => f.FileName, f => f.Side);

            await Assert.That(sides["client-thing.jar"]).IsEqualTo(ModSide.ClientOnly);
            await Assert.That(sides["server-thing.jar"]).IsEqualTo(ModSide.ServerOnly);
            await Assert.That(sides["both-thing.jar"]).IsEqualTo(ModSide.Both);
        }

        [Test]
        public async Task Mrpack_AClientUnsupportedEntry_IsKeptRatherThanSkipped()
        {
            // It used to be discarded, because HOPPER only fed game clients. A dedicated server
            // wants exactly this jar.
            using var archive = ArchiveOf(("modrinth.index.json", """
                {"formatVersion":1,"game":"minecraft","files":[
                  {"path":"mods/server-side.jar","hashes":{"sha1":"a"},"env":{"client":"unsupported","server":"required"},
                   "downloads":["https://cdn.modrinth.com/a.jar"],"fileSize":1}
                ]}
                """));

            var plan = ModrinthPlanner.Plan(archive, string.Empty, PackPlanContext.Default);

            await Assert.That(plan.Files.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "server-side.jar" });
            await Assert.That(plan.Files.Single().Side).IsEqualTo(ModSide.ServerOnly);
        }

        [Test]
        public async Task Mrpack_OverrideFolders_NameTheSide()
        {
            using var archive = ArchiveOf(
                ("modrinth.index.json", """{"formatVersion":1,"game":"minecraft","files":[]}"""),
                ("overrides/mods/shared.jar", "PK shared"),
                ("client-overrides/mods/client.jar", "PK client"),
                ("server-overrides/mods/server.jar", "PK server"));

            var plan = ModrinthPlanner.Plan(archive, string.Empty, PackPlanContext.Default);
            var sides = plan.Files.ToDictionary(f => f.FileName, f => f.Side);

            await Assert.That(sides.Keys.Order().ToList())
                .IsEquivalentTo(new[] { "client.jar", "server.jar", "shared.jar" });
            await Assert.That(sides["shared.jar"]).IsEqualTo(ModSide.Both);
            await Assert.That(sides["client.jar"]).IsEqualTo(ModSide.ClientOnly);
            await Assert.That(sides["server.jar"]).IsEqualTo(ModSide.ServerOnly);
        }

        // ---- the jar's own declaration -----------------------------------------------------

        [Test]
        [Arguments("client", ModSide.ClientOnly)]
        [Arguments("server", ModSide.ServerOnly)]
        [Arguments("*", ModSide.Both)]
        public async Task FabricJson_EnvironmentBecomesTheSide(string environment, ModSide expected)
        {
            var json = JsonSerializer.Serialize(new { schemaVersion = 1, id = "thing", environment });

            await Assert.That(ModSideReader.FromFabricEnvironment(json)).IsEqualTo(expected);
        }

        [Test]
        public async Task FabricJson_NoEnvironment_IsBoth()
        {
            await Assert.That(ModSideReader.FromFabricEnvironment("""{"schemaVersion":1,"id":"thing"}"""))
                .IsEqualTo(ModSide.Both);
        }

        [Test]
        public async Task QuiltJson_EnvironmentBecomesTheSide()
        {
            const string json = """{"quilt_loader":{"id":"thing","minecraft":{"environment":"client"}}}""";

            await Assert.That(ModSideReader.FromQuiltEnvironment(json)).IsEqualTo(ModSide.ClientOnly);
        }

        [Test]
        [Arguments("not json at all")]
        [Arguments("{")]
        [Arguments("[]")]
        public async Task HostileMetadata_IsBothRatherThanAThrow(string text)
        {
            await Assert.That(ModSideReader.FromFabricEnvironment(text)).IsEqualTo(ModSide.Both);
            await Assert.That(ModSideReader.FromQuiltEnvironment(text)).IsEqualTo(ModSide.Both);
        }

        [Test]
        public async Task Jar_DeclaringClientOnly_IsReadFromTheArchive()
        {
            var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var stream = archive.CreateEntry("fabric.mod.json").Open();
                stream.Write(Encoding.UTF8.GetBytes("""{"schemaVersion":1,"id":"sodium","environment":"client"}"""));
            }

            buffer.Position = 0;
            await Assert.That(ModSideReader.Read(buffer)).IsEqualTo(ModSide.ClientOnly);
        }

        [Test]
        public async Task Jar_WithNoMetadataAtAll_IsBoth()
        {
            var buffer = new MemoryStream();
            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var stream = archive.CreateEntry("META-INF/MANIFEST.MF").Open();
                stream.Write("Manifest-Version: 1.0\n"u8);
            }

            buffer.Position = 0;
            await Assert.That(ModSideReader.Read(buffer)).IsEqualTo(ModSide.Both);
        }

        [Test]
        public async Task NotAZip_IsBothRatherThanAThrow()
        {
            using var notAJar = new MemoryStream(Encoding.UTF8.GetBytes("this is not a zip"));

            await Assert.That(ModSideReader.Read(notAJar)).IsEqualTo(ModSide.Both);
        }
    }
}
