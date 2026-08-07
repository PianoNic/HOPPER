using System.Text;
using HOPPER.Application.Command.Mods;
using HOPPER.Application.Imports;
using HOPPER.Application.Maintenance;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using HOPPER.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HOPPER.Tests.Maintenance
{
    public class BlobReclaimServiceTests
    {
        private static readonly TimeSpan Grace = TimeSpan.FromHours(1);

        private sealed class StubUser(string? name) : ICurrentUserService
        {
            public string? Name { get; } = name;
        }

        private sealed class Fixture : IAsyncDisposable
        {
            public string Root { get; } = Path.Combine(Path.GetTempPath(), "hopper-reclaim-" + Guid.NewGuid().ToString("N"));

            public IConfiguration Configuration { get; }

            public FileSystemBlobStorage Blobs { get; }

            public HopperDbContext Db { get; private set; } = null!;

            private Fixture()
            {
                Directory.CreateDirectory(Root);

                Configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Blobs:Directory"] = Root,
                        ["Hopper:BlobReclaimGrace"] = "01:00:00",
                    })
                    .Build();

                Blobs = new FileSystemBlobStorage(Configuration);
                Staging = new ImportStaging(Configuration);
            }

            public static async Task<Fixture> CreateAsync()
            {
                var fixture = new Fixture();
                fixture.Db = PostgresHarness.Context(await PostgresHarness.NewMigratedDatabaseAsync());
                return fixture;
            }

            public ImportStaging Staging { get; }

            public BlobReclaimer Reclaimer() =>
                new(Db, Blobs, Staging, Configuration, NullLogger<BlobReclaimer>.Instance);

            public Task<ReclaimReport> SweepAsync() => Reclaimer().SweepAsync(DateTime.UtcNow);

            public Task<ReclaimReport> SweepAfterRestartAsync() =>
                Reclaimer().SweepAsync(DateTime.UtcNow, afterRestart: true);

            public async Task<Guid> SeedServerAsync(string suffix)
            {
                var server = new Server
                {
                    Name = $"Server {suffix}",
                    Slug = $"server-{suffix}",
                    Token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                };

                Db.Servers.Add(server);
                await Db.SaveChangesAsync();
                return server.Id;
            }

            public async Task<string> StoreAsync(Guid serverId, string fileName, string marker)
            {
                var handler = new UploadModsCommandHandler(Db, Blobs, new StubUser(null), TestLimits.Config);
                await handler.Handle(
                    new UploadModsCommand(serverId, [new UploadFile(fileName, new MemoryStream(Encoding.UTF8.GetBytes($"PK jar {marker}")))]),
                    CancellationToken.None);

                return (await Db.Mods.AsNoTracking().SingleAsync(m => m.ServerId == serverId && m.FileName == fileName)).Sha256;
            }

            public async Task<string> OrphanBlobAsync(string marker)
            {
                var (sha, _) = await Blobs.StoreAsync(
                    new MemoryStream(Encoding.UTF8.GetBytes($"orphan {marker}")), TestLimits.MaxBytes);
                return sha;
            }

            public string BlobPath(string sha) => Path.Combine(Root, sha[..2], sha[2..4], sha);

            public string WriteScratch(string directory, string name, string content)
            {
                var folder = Path.Combine(Root, directory);
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, name);
                File.WriteAllText(path, content);
                return path;
            }

            public void Age(string path) =>
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow - Grace - TimeSpan.FromMinutes(5));

            public void AgeDirectory(string path) =>
                Directory.SetLastWriteTimeUtc(path, DateTime.UtcNow - Grace - TimeSpan.FromMinutes(5));

            public async ValueTask DisposeAsync()
            {
                await Db.DisposeAsync();
                try { Directory.Delete(Root, recursive: true); } catch { }
            }
        }

        [Test]
        public async Task Sweep_UnreferencedBlobPastTheGrace_IsDeleted()
        {
            await using var fixture = await Fixture.CreateAsync();
            var sha = await fixture.OrphanBlobAsync("gone");
            fixture.Age(fixture.BlobPath(sha));

            var report = await fixture.SweepAsync();

            await Assert.That(report.Blobs).IsEqualTo(1);
            await Assert.That(fixture.Blobs.Exists(sha)).IsFalse();
        }

        [Test]
        public async Task Sweep_UnreferencedBlobInsideTheGrace_IsKept()
        {
            await using var fixture = await Fixture.CreateAsync();
            var sha = await fixture.OrphanBlobAsync("fresh");

            var report = await fixture.SweepAsync();

            await Assert.That(report.Blobs).IsEqualTo(0);
            await Assert.That(fixture.Blobs.Exists(sha)).IsTrue();
        }

        [Test]
        public async Task Sweep_BlobReferencedOnlyByAnotherServer_IsKept()
        {
            await using var fixture = await Fixture.CreateAsync();
            var a = await fixture.SeedServerAsync("a");
            var b = await fixture.SeedServerAsync("b");

            await fixture.StoreAsync(a, "jei.jar", "shared");
            var sha = await fixture.StoreAsync(b, "jei.jar", "shared");

            var onA = await fixture.Db.Mods.SingleAsync(m => m.ServerId == a);
            fixture.Db.Mods.Remove(onA);
            await fixture.Db.SaveChangesAsync();

            fixture.Age(fixture.BlobPath(sha));

            var report = await fixture.SweepAsync();

            await Assert.That(report.Blobs).IsEqualTo(0);
            await Assert.That(fixture.Blobs.Exists(sha)).IsTrue();
        }

        [Test]
        public async Task Sweep_ReferencedBlobPastTheGrace_IsKept()
        {
            await using var fixture = await Fixture.CreateAsync();
            var serverId = await fixture.SeedServerAsync("a");
            var sha = await fixture.StoreAsync(serverId, "jei.jar", "kept");
            fixture.Age(fixture.BlobPath(sha));

            var report = await fixture.SweepAsync();

            await Assert.That(report.Blobs).IsEqualTo(0);
            await Assert.That(fixture.Blobs.Exists(sha)).IsTrue();
        }

        [Test]
        public async Task Sweep_StaleTempPartFile_IsDeleted()
        {
            await using var fixture = await Fixture.CreateAsync();
            var part = fixture.WriteScratch("tmp", $"{Guid.NewGuid():N}.part", "half an upload");
            fixture.Age(part);

            var report = await fixture.SweepAsync();

            await Assert.That(report.Scratch).IsEqualTo(1);
            await Assert.That(File.Exists(part)).IsFalse();
        }

        [Test]
        public async Task Sweep_FreshTempPartFile_IsKept()
        {
            await using var fixture = await Fixture.CreateAsync();
            var part = fixture.WriteScratch("tmp", $"{Guid.NewGuid():N}.part", "an upload in flight");

            var report = await fixture.SweepAsync();

            await Assert.That(report.Scratch).IsEqualTo(0);
            await Assert.That(File.Exists(part)).IsTrue();
        }

        [Test]
        public async Task Sweep_StaleExportScratchFile_IsDeleted()
        {
            await using var fixture = await Fixture.CreateAsync();
            var export = fixture.WriteScratch("exports", $"{Guid.NewGuid():N}.tmp", "half an export");
            fixture.Age(export);

            var report = await fixture.SweepAsync();

            await Assert.That(report.Scratch).IsEqualTo(1);
            await Assert.That(File.Exists(export)).IsFalse();
        }

        [Test]
        public async Task Sweep_StagedPackOfACompletedImport_IsDeleted()
        {
            await using var fixture = await Fixture.CreateAsync();
            var serverId = await fixture.SeedServerAsync("a");
            var import = await SeedImportAsync(fixture, serverId, ImportStatus.Completed);

            var pack = fixture.WriteScratch("imports", $"{import:N}.pack", "a staged pack");
            fixture.Age(pack);

            var report = await fixture.SweepAsync();

            await Assert.That(report.StagedPacks).IsEqualTo(1);
            await Assert.That(File.Exists(pack)).IsFalse();
        }

        [Test]
        public async Task Sweep_StagedPackOfARunningImport_IsKept()
        {
            await using var fixture = await Fixture.CreateAsync();
            var serverId = await fixture.SeedServerAsync("a");
            var import = await SeedImportAsync(fixture, serverId, ImportStatus.Running);

            var pack = fixture.WriteScratch("imports", $"{import:N}.pack", "a live pack");
            fixture.Age(pack);

            var report = await fixture.SweepAsync();

            await Assert.That(report.StagedPacks).IsEqualTo(0);
            await Assert.That(File.Exists(pack)).IsTrue();
        }

        [Test]
        public async Task Sweep_StagedPackOfAQueuedImport_IsKept()
        {
            await using var fixture = await Fixture.CreateAsync();
            var serverId = await fixture.SeedServerAsync("a");
            var import = await SeedImportAsync(fixture, serverId, ImportStatus.Queued);

            var pack = fixture.WriteScratch("imports", $"{import:N}.pack", "a queued pack");
            fixture.Age(pack);

            var report = await fixture.SweepAsync();

            await Assert.That(report.StagedPacks).IsEqualTo(0);
            await Assert.That(File.Exists(pack)).IsTrue();
        }

        [Test]
        public async Task Sweep_StagedPackWithNoImportRowAtAll_IsDeleted()
        {
            await using var fixture = await Fixture.CreateAsync();
            var pack = fixture.WriteScratch("imports", $"{Guid.NewGuid():N}.pack", "nobody's pack");
            fixture.Age(pack);

            var report = await fixture.SweepAsync();

            await Assert.That(report.StagedPacks).IsEqualTo(1);
            await Assert.That(File.Exists(pack)).IsFalse();
        }

        [Test]
        public async Task Sweep_WorkDirectoryOfACompletedImport_IsDeleted()
        {
            await using var fixture = await Fixture.CreateAsync();
            var serverId = await fixture.SeedServerAsync("a");
            var import = await SeedImportAsync(fixture, serverId, ImportStatus.Failed);

            var work = Path.Combine(fixture.Root, "imports", import.ToString("N"));
            Directory.CreateDirectory(work);
            await File.WriteAllTextAsync(Path.Combine(work, "download.part"), "half a jar");
            fixture.AgeDirectory(work);

            var report = await fixture.SweepAsync();

            await Assert.That(report.StagedPacks).IsEqualTo(1);
            await Assert.That(Directory.Exists(work)).IsFalse();
        }

        [Test]
        public async Task Sweep_DoesNotDescendIntoImportsOrExportsLookingForBlobs()
        {
            await using var fixture = await Fixture.CreateAsync();

            var sixtyFourHex = new string('a', 64);
            var exportsShaped = Path.Combine(fixture.Root, "exports", "aa", "bb");
            Directory.CreateDirectory(exportsShaped);
            var decoy = Path.Combine(exportsShaped, sixtyFourHex);
            await File.WriteAllTextAsync(decoy, "not a blob");
            fixture.Age(decoy);

            var report = await fixture.SweepAsync();

            await Assert.That(report.Blobs).IsEqualTo(0);
            await Assert.That(File.Exists(decoy)).IsTrue();
        }

        [Test]
        public async Task FirstPass_RunningImportFromAPreviousProcess_IsFailedAndItsPackCleanedUp()
        {
            await using var fixture = await Fixture.CreateAsync();
            var serverId = await fixture.SeedServerAsync("a");
            var import = await SeedImportAsync(fixture, serverId, ImportStatus.Running);

            var pack = fixture.WriteScratch("imports", $"{import:N}.pack", "a pack nobody is reading");

            var report = await fixture.SweepAfterRestartAsync();

            await Assert.That(report.Imports).IsEqualTo(1);
            await Assert.That(File.Exists(pack)).IsFalse();

            var row = await fixture.Db.ModImports.AsNoTracking().SingleAsync(i => i.Id == import);
            await Assert.That(row.Status).IsEqualTo(ImportStatus.Failed);
            await Assert.That(row.Error).Contains("restart");
            await Assert.That(row.CompletedAt).IsNotNull();
        }

        [Test]
        public async Task FirstPass_QueuedImportFromAPreviousProcess_IsFailed()
        {
            await using var fixture = await Fixture.CreateAsync();
            var serverId = await fixture.SeedServerAsync("a");
            var import = await SeedImportAsync(fixture, serverId, ImportStatus.Queued);

            var report = await fixture.SweepAfterRestartAsync();

            await Assert.That(report.Imports).IsEqualTo(1);

            var row = await fixture.Db.ModImports.AsNoTracking().SingleAsync(i => i.Id == import);
            await Assert.That(row.Status).IsEqualTo(ImportStatus.Failed);
        }

        [Test]
        public async Task FirstPass_CompletedImport_IsLeftAlone()
        {
            await using var fixture = await Fixture.CreateAsync();
            var serverId = await fixture.SeedServerAsync("a");
            var import = await SeedImportAsync(fixture, serverId, ImportStatus.Completed);

            var report = await fixture.SweepAfterRestartAsync();

            await Assert.That(report.Imports).IsEqualTo(0);

            var row = await fixture.Db.ModImports.AsNoTracking().SingleAsync(i => i.Id == import);
            await Assert.That(row.Status).IsEqualTo(ImportStatus.Completed);
            await Assert.That(row.Error).IsNull();
        }

        [Test]
        public async Task LaterPass_RunningImportWithAFreshUpdatedAt_IsLeftAlone()
        {
            await using var fixture = await Fixture.CreateAsync();
            var serverId = await fixture.SeedServerAsync("a");
            var import = await SeedImportAsync(fixture, serverId, ImportStatus.Running);

            var report = await fixture.SweepAsync();

            await Assert.That(report.Imports).IsEqualTo(0);

            var row = await fixture.Db.ModImports.AsNoTracking().SingleAsync(i => i.Id == import);
            await Assert.That(row.Status).IsEqualTo(ImportStatus.Running);
        }

        [Test]
        public async Task LaterPass_RunningImportOlderThanTheStallTimeout_IsFailed()
        {
            await using var fixture = await Fixture.CreateAsync();
            var serverId = await fixture.SeedServerAsync("a");
            var import = await SeedImportAsync(fixture, serverId, ImportStatus.Running);

            await fixture.Db.ModImports
                .Where(i => i.Id == import)
                .ExecuteUpdateAsync(s => s.SetProperty(i => i.UpdatedAt, DateTime.UtcNow.AddHours(-6)));

            var report = await fixture.SweepAsync();

            await Assert.That(report.Imports).IsEqualTo(1);

            var row = await fixture.Db.ModImports.AsNoTracking().SingleAsync(i => i.Id == import);
            await Assert.That(row.Status).IsEqualTo(ImportStatus.Failed);
            await Assert.That(row.Error).Contains("stopped responding");
        }

        private static async Task<Guid> SeedImportAsync(Fixture fixture, Guid serverId, ImportStatus status)
        {
            var import = new ModImport
            {
                ServerId = serverId,
                SourceName = "pack.mrpack",
                SourceKind = ImportSourceKind.Upload,
                Status = status,
            };

            fixture.Db.ModImports.Add(import);
            await fixture.Db.SaveChangesAsync();
            return import.Id;
        }
    }
}
