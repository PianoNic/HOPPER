using System.IO.Compression;
using System.Text;
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
    /// The exporters and the planners read and write the same three formats from opposite ends, and
    /// nothing forces them to agree - they were written months apart against a published schema rather
    /// than against each other. So the cheapest real check is that HOPPER's own export imports back
    /// into HOPPER: PackDetector has to recognise it, and the matching planner has to find every jar.
    ///
    /// This catches the mistakes a schema test cannot, because they are about placement rather than
    /// content: an index one directory too deep, an instance zip that detects as a Modrinth pack, a
    /// game directory spelled ".minecraft" when the reader prefers "minecraft".
    /// </summary>
    public class ExportRoundTripTests
    {
        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hopper-roundtrip-" + Guid.NewGuid().ToString("N"));
            public TempDir() => Directory.CreateDirectory(Path);
            public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
        }

        private sealed class KeylessCurseForge : ICurseForgeClient
        {
            public bool IsConfigured => false;

            public Task<IReadOnlyDictionary<int, CurseForgeFile>> ResolveAsync(IReadOnlyList<int> fileIds, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyDictionary<int, CurseForgeFile>>(new Dictionary<int, CurseForgeFile>());

            public Task<Uri?> FindOnModrinthBySha1Async(string sha1, CancellationToken cancellationToken) =>
                Task.FromResult<Uri?>(null);
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
                    .UseInMemoryDatabase($"hopper-roundtrip-{Guid.NewGuid():N}")
                    .Options);

                Db.Servers.Add(new Server
                {
                    Id = ServerId,
                    Name = "Round Trip",
                    Slug = "round-trip",
                    Token = new string('a', 64),
                    MinecraftVersion = "1.20.1",
                    Loader = ModLoader.Forge,
                    LoaderVersion = "47.4.10",
                });

                Add("jei.jar", ModSource.Modrinth, "u6dRKJwZ", "mcC2LhSG");
                Add("create.jar", ModSource.Modrinth, "LNytGWDc", "iRckjniU");
                Add("hand-uploaded.jar", ModSource.Manual, null, null);

                Db.SaveChanges();
            }

            private void Add(string fileName, ModSource source, string? projectId, string? versionId)
            {
                var bytes = Encoding.UTF8.GetBytes($"PK {fileName}");
                var (sha256, size) = Blobs.SaveAsync(new MemoryStream(bytes)).GetAwaiter().GetResult();

                Db.Mods.Add(new Mod
                {
                    ServerId = ServerId,
                    FileName = fileName,
                    Sha256 = sha256,
                    Size = size,
                    Source = source,
                    ProjectId = projectId,
                    VersionId = versionId,
                    DownloadUrl = source == ModSource.Modrinth
                        ? $"https://cdn.modrinth.com/data/{projectId}/versions/{versionId}/{fileName}"
                        : null,
                    Sha1 = source == ModSource.Modrinth ? new string('1', 40) : null,
                    Sha512 = source == ModSource.Modrinth ? new string('5', 128) : null,
                });
            }

            public void Dispose()
            {
                Db.Dispose();
                Dir.Dispose();
            }
        }

        /// <summary>Buffers the export so it can be opened as a ZipArchive, which needs to seek to the
        /// central directory at the end.</summary>
        private static async Task<ZipArchive> ReopenAsync(PackExportResult result)
        {
            var buffer = new MemoryStream();
            await using (result.Content)
                await result.Content.CopyToAsync(buffer);

            buffer.Position = 0;
            return new ZipArchive(buffer, ZipArchiveMode.Read);
        }

        [Test]
        public async Task ExportedMrpack_DetectsAsModrinthAndPlansEveryJarBack()
        {
            using var fixture = new Fixture();
            var exporter = new MrpackExporter(fixture.Db, fixture.Blobs, fixture.Configuration);

            using var archive = await ReopenAsync(await exporter.ExportAsync(fixture.ServerId, CancellationToken.None));

            var detection = PackDetector.Detect(archive);
            await Assert.That(detection.Format).IsEqualTo(PackFormat.Modrinth);
            await Assert.That(detection.Prefix).IsEqualTo(string.Empty);

            var plan = ModrinthPlanner.Plan(archive, detection.Prefix);

            // Two from files[] with their CDN URLs, one out of overrides/mods/ as a zip entry.
            await Assert.That(plan.Files.Select(f => f.FileName).Order().ToList())
                .IsEquivalentTo(new[] { "create.jar", "hand-uploaded.jar", "jei.jar" });

            var jei = plan.Files.Single(f => f.FileName == "jei.jar");
            await Assert.That(jei.Downloads.Any(d => d.Host == "cdn.modrinth.com")).IsTrue();

            var manual = plan.Files.Single(f => f.FileName == "hand-uploaded.jar");
            await Assert.That(manual.ZipEntry).IsNotNull();
        }

        [Test]
        public async Task ExportedPrismInstance_DetectsAsAnInstanceAndPlansEveryJarBack()
        {
            // The two rules this proves together: no modrinth.index.json in the archive (or detection
            // would pick Modrinth first and never look at instance.cfg), and "minecraft/" rather than
            // ".minecraft/", which is what the planner prefers.
            using var fixture = new Fixture();
            var exporter = new PrismInstanceExporter(fixture.Db, fixture.Blobs, fixture.Configuration);

            using var archive = await ReopenAsync(await exporter.ExportAsync(fixture.ServerId, CancellationToken.None));

            var detection = PackDetector.Detect(archive);
            await Assert.That(detection.Format).IsEqualTo(PackFormat.PrismInstance);

            var plan = PrismPlanner.Plan(archive, detection.Prefix);

            await Assert.That(plan.Files.Select(f => f.FileName).Order().ToList())
                .IsEquivalentTo(new[] { "create.jar", "hand-uploaded.jar", "jei.jar" });
        }

        [Test]
        public async Task ExportedCurseForgeZip_DetectsAsCurseForgeAndPlansEveryJarBack()
        {
            using var fixture = new Fixture();
            var exporter = new CurseForgePackExporter(fixture.Db, fixture.Blobs, fixture.Configuration);

            using var archive = await ReopenAsync(await exporter.ExportAsync(fixture.ServerId, CancellationToken.None));

            var detection = PackDetector.Detect(archive);
            await Assert.That(detection.Format).IsEqualTo(PackFormat.CurseForge);

            var plan = await CurseForgePlanner.PlanAsync(
                archive, detection.Prefix, new KeylessCurseForge(), CancellationToken.None);

            // files[] is empty by construction, so every jar arrives out of overrides and nothing ends
            // up pending - which is precisely the property that makes an empty files[] acceptable.
            await Assert.That(plan.Files.Select(f => f.FileName).Order().ToList())
                .IsEquivalentTo(new[] { "create.jar", "hand-uploaded.jar", "jei.jar" });

            await Assert.That(plan.Pending).IsEmpty();
        }
    }
}
