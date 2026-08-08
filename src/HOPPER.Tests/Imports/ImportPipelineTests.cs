using System.IO.Compression;
using System.Net;
using System.Text;
using HOPPER.Application.Imports;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Services;
using HOPPER.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HOPPER.Tests.Imports
{
    public class ImportPipelineTests
    {
        private sealed class KeylessCurseForge : ICurseForgeClient
        {
            public bool IsConfigured => false;

            public Task<IReadOnlyDictionary<int, CurseForgeFile>> ResolveAsync(IReadOnlyList<int> fileIds, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyDictionary<int, CurseForgeFile>>(new Dictionary<int, CurseForgeFile>());

            public Task<Uri?> FindOnModrinthBySha1Async(string sha1, CancellationToken cancellationToken) =>
                Task.FromResult<Uri?>(null);
        }

        private sealed class ConfiguredCurseForge(params CurseForgeFile[] files) : ICurseForgeClient
        {
            public bool IsConfigured => true;

            public Task<IReadOnlyDictionary<int, CurseForgeFile>> ResolveAsync(IReadOnlyList<int> fileIds, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyDictionary<int, CurseForgeFile>>(
                    files.Where(f => fileIds.Contains(f.FileId)).ToDictionary(f => f.FileId));

            public Task<Uri?> FindOnModrinthBySha1Async(string sha1, CancellationToken cancellationToken) =>
                Task.FromResult<Uri?>(null);
        }

        private sealed class FixedResponseHandler(int bodyBytes) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(new byte[bodyBytes]),
                });
        }

        private sealed class StubHttpClientFactory(int bodyBytes) : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) => new(new FixedResponseHandler(bodyBytes));
        }

        private sealed class Fixture : IAsyncDisposable
        {
            public string Root { get; } = Path.Combine(Path.GetTempPath(), "hopper-import-" + Guid.NewGuid().ToString("N"));

            public IConfiguration Configuration { get; private set; } = null!;

            public FileSystemBlobStorage Blobs { get; private set; } = null!;

            public ImportStaging Staging { get; private set; } = null!;

            public HopperDbContext Db { get; private set; } = null!;

            public Guid ServerId { get; private set; }

            public int DownloadBodyBytes { get; set; } = 16;

            public ICurseForgeClient CurseForge { get; set; } = new KeylessCurseForge();

            public static async Task<Fixture> CreateAsync(
                ModLoader loader = ModLoader.Unknown,
                string? minecraftVersion = null,
                params (string Key, string Value)[] settings)
            {
                var fixture = new Fixture();
                Directory.CreateDirectory(fixture.Root);

                var values = new Dictionary<string, string?> { ["Blobs:Directory"] = fixture.Root };
                foreach (var (key, value) in settings)
                    values[key] = value;

                fixture.Configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
                fixture.Blobs = new FileSystemBlobStorage(fixture.Configuration);
                fixture.Staging = new ImportStaging(fixture.Configuration);
                fixture.Db = PostgresHarness.Context(await PostgresHarness.NewMigratedDatabaseAsync());

                var server = new Server
                {
                    Name = "Test",
                    Slug = "test",
                    Token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
                    MinecraftVersion = minecraftVersion,
                    Loader = loader,
                };

                fixture.Db.Servers.Add(server);
                await fixture.Db.SaveChangesAsync();
                fixture.ServerId = server.Id;

                return fixture;
            }

            public PackImporter Importer() => new(
                Db,
                Blobs,
                Staging,
                new StubHttpClientFactory(DownloadBodyBytes),
                CurseForge,
                Configuration,
                NullLogger<PackImporter>.Instance);

            public async Task<Guid> StageAsync(byte[] pack, string? createdBy = null)
            {
                var import = new ModImport
                {
                    ServerId = ServerId,
                    SourceName = "pack.zip",
                    SourceKind = ImportSourceKind.Upload,
                    Status = ImportStatus.Queued,
                    CreatedBy = createdBy,
                };

                Db.ModImports.Add(import);
                await Db.SaveChangesAsync();

                await Staging.StageAsync(import.Id, new MemoryStream(pack), long.MaxValue, CancellationToken.None);

                return import.Id;
            }

            public async Task<Guid> QueueUrlAsync(string url)
            {
                var import = new ModImport
                {
                    ServerId = ServerId,
                    SourceName = url,
                    SourceKind = ImportSourceKind.Url,
                    Status = ImportStatus.Queued,
                };

                Db.ModImports.Add(import);
                await Db.SaveChangesAsync();

                return import.Id;
            }

            public async Task<ModImport> RunAsync(Guid importId)
            {
                await Importer().RunAsync(importId, CancellationToken.None);

                Db.ChangeTracker.Clear();
                return await Db.ModImports.AsNoTracking().SingleAsync(i => i.Id == importId);
            }

            public async ValueTask DisposeAsync()
            {
                await Db.DisposeAsync();
                try { Directory.Delete(Root, recursive: true); } catch { }
            }
        }

        private static byte[] ZipOf(params (string Path, string Content)[] entries)
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

            return buffer.ToArray();
        }

        private static byte[] PrismInstance(string loaderUid, string loaderVersion, string minecraftVersion) =>
            ZipOf(
                ("instance.cfg", $"[General]\nname={minecraftVersion}\n"),
                ("mmc-pack.json", $$"""
                 {"formatVersion":1,"components":[
                   {"uid":"net.minecraft","version":"{{minecraftVersion}}","important":true},
                   {"uid":"{{loaderUid}}","version":"{{loaderVersion}}"}
                 ]}
                 """),
                ("minecraft/mods/jei.jar", "PK jei"));

        [Test]
        public async Task Import_OfAPlainJarZip_CompletesAndStoresEveryJar()
        {
            await using var fixture = await Fixture.CreateAsync();
            var importId = await fixture.StageAsync(ZipOf(("jei.jar", "PK jei"), ("rei.jar", "PK rei")));

            var row = await fixture.RunAsync(importId);

            await Assert.That(row.Status).IsEqualTo(ImportStatus.Completed);
            await Assert.That(row.ImportedCount).IsEqualTo(2);
            await Assert.That(await fixture.Db.Mods.CountAsync(m => m.ServerId == fixture.ServerId)).IsEqualTo(2);
            await Assert.That(File.Exists(fixture.Staging.PackPath(importId))).IsFalse();
        }

        [Test]
        public async Task Import_WhenTheFinalSaveFails_StillWritesATerminalStatus()
        {
            await using var fixture = await Fixture.CreateAsync();
            var importId = await fixture.StageAsync(ZipOf(("jei.jar", "PK jei")), createdBy: new string('u', 300));

            var row = await fixture.RunAsync(importId);

            await Assert.That(row.Status).IsEqualTo(ImportStatus.Failed);
            await Assert.That(row.CompletedAt).IsNotNull();
            await Assert.That(row.Error).IsNotNull();
        }

        [Test]
        public async Task Import_WhenTheFinalSaveFails_StillCleansUpTheStagedPack()
        {
            await using var fixture = await Fixture.CreateAsync();
            var importId = await fixture.StageAsync(ZipOf(("jei.jar", "PK jei")), createdBy: new string('u', 300));

            await fixture.RunAsync(importId);

            await Assert.That(File.Exists(fixture.Staging.PackPath(importId))).IsFalse();
            await Assert.That(Directory.Exists(fixture.Staging.WorkDirectory(importId))).IsFalse();
        }

        [Test]
        public async Task Import_WhenTheFinalSaveFails_PublishesNoBlob()
        {
            await using var fixture = await Fixture.CreateAsync();
            var importId = await fixture.StageAsync(ZipOf(("jei.jar", "PK jei")), createdBy: new string('u', 300));

            await fixture.RunAsync(importId);

            await Assert.That(fixture.Blobs.EnumerateBlobs().Any()).IsFalse();
        }

        [Test]
        public async Task Import_ThatThrewMidWay_IsFailedWithTheMessageAndTheStagedPackIsGone()
        {
            await using var fixture = await Fixture.CreateAsync();
            var importId = await fixture.StageAsync(ZipOf(("notes.txt", "nothing useful here")));

            var row = await fixture.RunAsync(importId);

            await Assert.That(row.Status).IsEqualTo(ImportStatus.Failed);
            await Assert.That(row.Error).Contains("Not a recognised");
            await Assert.That(File.Exists(fixture.Staging.PackPath(importId))).IsFalse();
        }

        [Test]
        public async Task Import_PlatformLoaderMismatch_IsFailedWithTheMismatchMessage()
        {
            await using var fixture = await Fixture.CreateAsync(ModLoader.Fabric, "1.21");
            var importId = await fixture.StageAsync(PrismInstance("net.minecraftforge", "47.4.20", "1.20.1"));

            var row = await fixture.RunAsync(importId);

            await Assert.That(row.Status).IsEqualTo(ImportStatus.Failed);
            await Assert.That(row.Error).Contains("Forge");
            await Assert.That(row.Error).Contains("Fabric");
            await Assert.That(await fixture.Db.Mods.AnyAsync(m => m.ServerId == fixture.ServerId)).IsFalse();
        }

        [Test]
        public async Task Import_PlatformVersionWarning_CompletesWithTheWarningInTheError()
        {
            await using var fixture = await Fixture.CreateAsync(ModLoader.Forge, "1.20.4");
            var importId = await fixture.StageAsync(PrismInstance("net.minecraftforge", "47.4.20", "1.20.1"));

            var row = await fixture.RunAsync(importId);

            await Assert.That(row.Status).IsEqualTo(ImportStatus.Completed);
            await Assert.That(row.ImportedCount).IsEqualTo(1);
            await Assert.That(row.Error).Contains("1.20.1");
            await Assert.That(row.Error).Contains("1.20.4");
        }

        [Test]
        public async Task Import_PlatformThatMatches_CompletesWithNoError()
        {
            await using var fixture = await Fixture.CreateAsync(ModLoader.Forge, "1.20.1");
            var importId = await fixture.StageAsync(PrismInstance("net.minecraftforge", "47.4.20", "1.20.1"));

            var row = await fixture.RunAsync(importId);

            await Assert.That(row.Status).IsEqualTo(ImportStatus.Completed);
            await Assert.That(row.Error).IsNull();
        }

        [Test]
        public async Task Import_MrpackOverrideReplacingAFilesEntry_StoresTheOverrideNotTheCdnCopy()
        {
            await using var fixture = await Fixture.CreateAsync();

            var importId = await fixture.StageAsync(ZipOf(
                ("modrinth.index.json", """
                 {"formatVersion":1,"game":"minecraft","files":[
                   {"path":"mods/patched.jar","hashes":{"sha1":"a"},"downloads":["https://cdn.modrinth.com/patched.jar"],"fileSize":1}
                 ]}
                 """),
                ("overrides/mods/patched.jar", "PK the patched one")));

            var row = await fixture.RunAsync(importId);

            await Assert.That(row.Status).IsEqualTo(ImportStatus.Completed);
            await Assert.That(row.ImportedCount).IsEqualTo(1);
            await Assert.That(row.SkippedCount).IsEqualTo(0);
            await Assert.That(row.PendingCount).IsEqualTo(0);

            var mod = await fixture.Db.Mods.AsNoTracking().SingleAsync(m => m.ServerId == fixture.ServerId);
            await Assert.That(mod.FileName).IsEqualTo("patched.jar");

            await using var stored = fixture.Blobs.OpenRead(mod.Sha256)!;
            using var reader = new StreamReader(stored);
            await Assert.That(await reader.ReadToEndAsync()).IsEqualTo("PK the patched one");
        }

        [Test]
        public async Task Import_ResponseLongerThanTheJarLimit_MarksThatFilePendingNotFailsTheImport()
        {
            await using var fixture = await Fixture.CreateAsync(
                ModLoader.Unknown, null, ("Hopper:MaxModBytes", "1024"));

            fixture.DownloadBodyBytes = 64 * 1024;

            var importId = await fixture.StageAsync(ZipOf(("modrinth.index.json", """
                {"formatVersion":1,"game":"minecraft","files":[
                  {"path":"mods/huge.jar","hashes":{"sha1":"a"},"downloads":["https://cdn.modrinth.com/huge.jar"],"fileSize":1}
                ]}
                """)));

            var row = await fixture.RunAsync(importId);

            await Assert.That(row.Status).IsEqualTo(ImportStatus.Completed);
            await Assert.That(row.PendingCount).IsEqualTo(1);

            var pending = await fixture.Db.PendingMods.AsNoTracking().SingleAsync(p => p.ImportId == importId);
            await Assert.That(pending.Reason).IsEqualTo(PendingReason.DownloadFailed);
            await Assert.That(pending.Detail).Contains("1024");
        }

        [Test]
        public async Task Import_OfACurseForgePack_CarriesTheProjectAndFileIdsOntoTheStoredMod()
        {
            await using var fixture = await Fixture.CreateAsync();

            fixture.CurseForge = new ConfiguredCurseForge(new CurseForgeFile(
                238222, 5678, "jei.jar", new Uri("https://edge.forgecdn.net/files/5/678/jei.jar"), 16, null, "Just Enough Items"));

            var importId = await fixture.StageAsync(ZipOf(("manifest.json", """
                {"manifestType":"minecraftModpack","manifestVersion":1,
                 "files":[{"projectID":238222,"fileID":5678,"required":true}]}
                """)));

            var row = await fixture.RunAsync(importId);

            await Assert.That(row.Status).IsEqualTo(ImportStatus.Completed);
            await Assert.That(row.ImportedCount).IsEqualTo(1);

            var mod = await fixture.Db.Mods.AsNoTracking().SingleAsync(m => m.ServerId == fixture.ServerId);

            await Assert.That(mod.Source).IsEqualTo(ModSource.CurseForge);
            await Assert.That(mod.ProjectId).IsEqualTo("238222");
            await Assert.That(mod.VersionId).IsEqualTo("5678");
            await Assert.That(mod.ProjectName).IsEqualTo("Just Enough Items");
            await Assert.That(mod.DownloadUrl).IsEqualTo("https://edge.forgecdn.net/files/5/678/jei.jar");
            await Assert.That(mod.Sha1).IsNotNull();
            await Assert.That(mod.Sha512).IsNotNull();
        }

        [Test]
        public async Task Import_OfAJarZip_LeavesTheStoredModWithoutProvenance()
        {
            await using var fixture = await Fixture.CreateAsync();
            var importId = await fixture.StageAsync(ZipOf(("jei.jar", "PK jei")));

            await fixture.RunAsync(importId);

            var mod = await fixture.Db.Mods.AsNoTracking().SingleAsync(m => m.ServerId == fixture.ServerId);

            await Assert.That(mod.Source).IsEqualTo(ModSource.Manual);
            await Assert.That(mod.ProjectId).IsNull();
            await Assert.That(mod.DownloadUrl).IsNull();
        }

        [Test]
        public async Task Import_FromAUrlOnAHostThatIsNotAllowed_FailsWithoutFetchingIt()
        {
            await using var fixture = await Fixture.CreateAsync();
            var importId = await fixture.QueueUrlAsync("https://169.254.169.254/latest/meta-data/");

            var row = await fixture.RunAsync(importId);

            await Assert.That(row.Status).IsEqualTo(ImportStatus.Failed);
            await Assert.That(row.Error).Contains("not allowed");
            await Assert.That(File.Exists(fixture.Staging.PackPath(importId))).IsFalse();
            await Assert.That(await fixture.Db.Mods.AnyAsync(m => m.ServerId == fixture.ServerId)).IsFalse();
        }

        [Test]
        public async Task Import_DeclaredEntryLengthsOverTheImportBudget_IsRejectedBeforeAnythingIsStored()
        {
            await using var fixture = await Fixture.CreateAsync(
                ModLoader.Unknown, null, ("Hopper:MaxImportBytes", "16"));

            var importId = await fixture.StageAsync(ZipOf(("jei.jar", new string('j', 4096))));

            var row = await fixture.RunAsync(importId);

            await Assert.That(row.Status).IsEqualTo(ImportStatus.Failed);
            await Assert.That(row.Error).Contains("MaxImportBytes");
            await Assert.That(await fixture.Db.Mods.AnyAsync(m => m.ServerId == fixture.ServerId)).IsFalse();
        }
    }
}
