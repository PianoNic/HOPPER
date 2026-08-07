using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HOPPER.Application.Exports;
using HOPPER.Application.Imports;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Tests.Exports
{
    /// <summary>
    /// A side survives an import and used to be lost on the way back out, so a pack that had been
    /// classified came home as if nothing ever had been. These go the whole way round.
    /// </summary>
    public class ExportSideRoundTripTests
    {
        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hopper-side-rt-" + Guid.NewGuid().ToString("N"));
            public TempDir() => Directory.CreateDirectory(Path);
            public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
        }

        private sealed class Fixture : IDisposable
        {
            public TempDir Dir { get; } = new();
            public HopperDbContext Db { get; }
            public FileSystemBlobStorage Blobs { get; }
            public IConfiguration Configuration { get; }
            public Guid ServerId { get; } = Guid.NewGuid();

            public Fixture()
            {
                Configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Blobs:Directory"] = Dir.Path })
                    .Build();

                Blobs = new FileSystemBlobStorage(Configuration);

                Db = new HopperDbContext(new DbContextOptionsBuilder<HopperDbContext>()
                    .UseInMemoryDatabase($"hopper-side-rt-{Guid.NewGuid():N}")
                    .Options);

                Db.Servers.Add(new Server
                {
                    Id = ServerId,
                    Name = "Side Round Trip",
                    Slug = "side-round-trip",
                    Token = new string('a', 64),
                    MinecraftVersion = "1.20.1",
                    Loader = ModLoader.Forge,
                    LoaderVersion = "47.4.10",
                });

                // One linked and one bundled per side: the two take different paths out, files[]
                // with an env for the linked, an override folder for the bundled.
                Add("linked-both.jar", ModSource.Modrinth, ModSide.Both, "aaaaaaaa");
                Add("linked-client.jar", ModSource.Modrinth, ModSide.ClientOnly, "bbbbbbbb");
                Add("linked-server.jar", ModSource.Modrinth, ModSide.ServerOnly, "cccccccc");
                Add("bundled-both.jar", ModSource.Manual, ModSide.Both, null);
                Add("bundled-client.jar", ModSource.Manual, ModSide.ClientOnly, null);
                Add("bundled-server.jar", ModSource.Manual, ModSide.ServerOnly, null);

                Db.SaveChanges();
            }

            private void Add(string fileName, ModSource source, ModSide side, string? projectId)
            {
                var bytes = Encoding.UTF8.GetBytes($"PK {fileName}");
                var (sha256, size) = Blobs.StoreAsync(new MemoryStream(bytes), TestLimits.MaxBytes).GetAwaiter().GetResult();

                Db.Mods.Add(new Mod
                {
                    ServerId = ServerId,
                    FileName = fileName,
                    Sha256 = sha256,
                    Size = size,
                    Source = source,
                    Side = side,
                    ProjectId = projectId,
                    VersionId = projectId is null ? null : "v1",
                    DownloadUrl = projectId is null
                        ? null
                        : $"https://cdn.modrinth.com/data/{projectId}/versions/v1/{fileName}",
                    Sha1 = projectId is null ? null : new string('1', 40),
                    Sha512 = projectId is null ? null : new string('5', 128),
                });
            }

            public void Dispose()
            {
                Db.Dispose();
                Dir.Dispose();
            }
        }

        private static async Task<ZipArchive> ExportAsync(Fixture fixture)
        {
            var exporter = new MrpackExporter(fixture.Db, fixture.Blobs, fixture.Configuration);
            var result = await exporter.ExportAsync(fixture.ServerId, CancellationToken.None);

            var buffer = new MemoryStream();
            await using (result.Content)
                await result.Content.CopyToAsync(buffer);

            buffer.Position = 0;
            return new ZipArchive(buffer, ZipArchiveMode.Read);
        }

        [Test]
        public async Task LinkedMods_CarryTheirSideAsEnv()
        {
            using var fixture = new Fixture();
            using var archive = await ExportAsync(fixture);

            using var index = JsonDocument.Parse(
                new StreamReader(archive.GetEntry("modrinth.index.json")!.Open()).ReadToEnd());

            var byName = index.RootElement.GetProperty("files").EnumerateArray()
                .ToDictionary(
                    f => f.GetProperty("path").GetString()!.Split('/')[^1],
                    f => (f.GetProperty("env").GetProperty("client").GetString(),
                          f.GetProperty("env").GetProperty("server").GetString()));

            await Assert.That(byName["linked-both.jar"]).IsEqualTo(("required", "required"));
            await Assert.That(byName["linked-client.jar"]).IsEqualTo(("required", "unsupported"));
            await Assert.That(byName["linked-server.jar"]).IsEqualTo(("unsupported", "required"));
        }

        [Test]
        public async Task BundledMods_GoToTheFolderForTheirSide()
        {
            using var fixture = new Fixture();
            using var archive = await ExportAsync(fixture);

            var entries = archive.Entries.Select(e => e.FullName).ToList();

            await Assert.That(entries).Contains("overrides/mods/bundled-both.jar");
            await Assert.That(entries).Contains("client-overrides/mods/bundled-client.jar");
            await Assert.That(entries).Contains("server-overrides/mods/bundled-server.jar");
        }

        [Test]
        public async Task Exported_ThenReimported_KeepsEverySide()
        {
            // The whole point. Before this, everything came back Both and the classification was
            // gone with nothing reporting it.
            using var fixture = new Fixture();
            using var archive = await ExportAsync(fixture);

            var detection = PackDetector.Detect(archive);
            await Assert.That(detection.Format).IsEqualTo(PackFormat.Modrinth);

            var plan = ModrinthPlanner.Plan(archive, detection.Prefix, PackPlanContext.Default);
            var sides = plan.Files.ToDictionary(f => f.FileName, f => f.Side);

            await Assert.That(sides["linked-both.jar"]).IsEqualTo(ModSide.Both);
            await Assert.That(sides["linked-client.jar"]).IsEqualTo(ModSide.ClientOnly);
            await Assert.That(sides["linked-server.jar"]).IsEqualTo(ModSide.ServerOnly);
            await Assert.That(sides["bundled-both.jar"]).IsEqualTo(ModSide.Both);
            await Assert.That(sides["bundled-client.jar"]).IsEqualTo(ModSide.ClientOnly);
            await Assert.That(sides["bundled-server.jar"]).IsEqualTo(ModSide.ServerOnly);
        }

        [Test]
        [Arguments(ModSide.Both)]
        [Arguments(ModSide.ClientOnly)]
        [Arguments(ModSide.ServerOnly)]
        public async Task WireAndSide_AreInverses(ModSide side)
        {
            // The reader and the writer live in one file so they are edited together; this is what
            // proves they still agree.
            var (client, server) = PackEnv.Wire(side);

            await Assert.That(PackEnv.Side(client, server)).IsEqualTo(side);
        }
    }
}
