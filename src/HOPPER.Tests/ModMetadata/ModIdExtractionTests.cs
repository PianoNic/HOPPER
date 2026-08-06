using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using HOPPER.Application.Command.Imports;
using HOPPER.Application.Command.Mods;
using HOPPER.Application.Command.Modrinth;
using HOPPER.Application.Imports;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using HOPPER.Tests.Modrinth;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace HOPPER.Tests.ModMetadata
{
    public class ModIdExtractionTests
    {
        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hopper-modids-" + Guid.NewGuid().ToString("N"));
            public TempDir() => Directory.CreateDirectory(Path);
            public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch {  } }
        }

        private sealed class StubUser(string? name) : ICurrentUserService
        {
            public string? Name { get; } = name;
        }

        private static HopperDbContext NewDb() =>
            new(new DbContextOptionsBuilder<HopperDbContext>()
                .UseInMemoryDatabase($"hopper-modids-{Guid.NewGuid():N}")
                .Options);

        private static IConfiguration ConfigIn(string root) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Blobs:Directory"] = root })
                .Build();

        private static FileSystemBlobStorage StorageIn(string root) => new(ConfigIn(root));

        internal static byte[] ForgeJar(string modId) => Zip(("META-INF/mods.toml", $"""
            modLoader="javafml"
            loaderVersion="[47,)"
            [[mods]]
            modId="{modId}"
            version="1.0.0"
            """));

        internal static byte[] Zip(params (string Name, string Content)[] entries)
        {
            var buffer = new MemoryStream();

            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, content) in entries)
                {
                    using var stream = archive.CreateEntry(name).Open();
                    stream.Write(Encoding.UTF8.GetBytes(content));
                }
            }

            return buffer.ToArray();
        }

        private static byte[] ZipOfJars(params (string Name, byte[] Bytes)[] jars)
        {
            var buffer = new MemoryStream();

            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var (name, bytes) in jars)
                {
                    using var stream = archive.CreateEntry(name).Open();
                    stream.Write(bytes);
                }
            }

            return buffer.ToArray();
        }

        [Test]
        public async Task Upload_LooseJar_StoresItsModIds()
        {
            using var dir = new TempDir();
            await using var db = NewDb();
            var handler = new UploadModsCommandHandler(db, StorageIn(dir.Path), new StubUser("alex"));

            await handler.Handle(new UploadModsCommand(Guid.NewGuid(),
                [new UploadFile("jei.jar", new MemoryStream(ForgeJar("jei")))]), CancellationToken.None);

            var row = await db.Mods.SingleAsync();
            await Assert.That(row.ModIds).IsEquivalentTo(new[] { "jei" });
        }

        [Test]
        public async Task Upload_JarInsideAZipBatch_StoresItsModIds()
        {
            using var dir = new TempDir();
            await using var db = NewDb();
            var handler = new UploadModsCommandHandler(db, StorageIn(dir.Path), new StubUser(null));

            await handler.Handle(new UploadModsCommand(Guid.NewGuid(),
            [
                new UploadFile("mods.zip", new MemoryStream(ZipOfJars(
                    ("jei.jar", ForgeJar("jei")),
                    ("rei.jar", ForgeJar("roughlyenoughitems"))))),
            ]), CancellationToken.None);

            var rows = await db.Mods.OrderBy(m => m.FileName).ToListAsync();
            await Assert.That(rows[0].ModIds).IsEquivalentTo(new[] { "jei" });
            await Assert.That(rows[1].ModIds).IsEquivalentTo(new[] { "roughlyenoughitems" });
        }

        [Test]
        public async Task Upload_MultiModJar_StoresEveryId()
        {
            using var dir = new TempDir();
            await using var db = NewDb();
            var handler = new UploadModsCommandHandler(db, StorageIn(dir.Path), new StubUser(null));

            var jar = Zip(("META-INF/mods.toml", """
                [[mods]]
                modId="embeddium"
                [[mods]]
                modId = "rubidium"

                [[dependencies.embeddium]]
                modId = "oculus"
                mandatory = false
                """));

            await handler.Handle(new UploadModsCommand(Guid.NewGuid(),
                [new UploadFile("embeddium.jar", new MemoryStream(jar))]), CancellationToken.None);

            var row = await db.Mods.SingleAsync();
            await Assert.That(row.ModIds).IsEquivalentTo(new[] { "embeddium", "rubidium" });
        }

        [Test]
        public async Task Upload_JarWithNoMetadata_StoresAnEmptySetNotNull()
        {
            using var dir = new TempDir();
            await using var db = NewDb();
            var handler = new UploadModsCommandHandler(db, StorageIn(dir.Path), new StubUser(null));

            await handler.Handle(new UploadModsCommand(Guid.NewGuid(),
                [new UploadFile("lib.jar", new MemoryStream(Zip(("com/example/Lib.class", "x"))))]),
                CancellationToken.None);

            var row = await db.Mods.SingleAsync();
            await Assert.That(row.ModIds).IsNotNull();
            await Assert.That(row.ModIds!).IsEmpty();
        }

        [Test]
        public async Task Upload_NonJarPayload_StoresAnEmptySetNotNull()
        {
            using var dir = new TempDir();
            await using var db = NewDb();
            var handler = new UploadModsCommandHandler(db, StorageIn(dir.Path), new StubUser(null));

            await handler.Handle(new UploadModsCommand(Guid.NewGuid(),
                [new UploadFile("fake.jar", new MemoryStream(Encoding.UTF8.GetBytes("PK pretend forge jar payload")))]),
                CancellationToken.None);

            var row = await db.Mods.SingleAsync();
            await Assert.That(row.ModIds).IsNotNull();
            await Assert.That(row.ModIds!).IsEmpty();
        }

        [Test]
        public async Task PackImport_StoresModIds()
        {
            using var dir = new TempDir();
            await using var db = NewDb();

            var configuration = ConfigIn(dir.Path);
            var staging = new ImportStaging(configuration);
            var serverId = Guid.NewGuid();

            var import = new ModImport
            {
                ServerId = serverId,
                SourceName = "pack.zip",
                SourceKind = ImportSourceKind.Upload,
                Status = ImportStatus.Queued,
                CreatedBy = "alex",
            };

            db.ModImports.Add(import);
            await db.SaveChangesAsync();

            await staging.StageAsync(
                import.Id,
                new MemoryStream(ZipOfJars(("jei.jar", ForgeJar("jei")))),
                long.MaxValue,
                CancellationToken.None);

            var importer = new PackImporter(
                db,
                StorageIn(dir.Path),
                staging,
                new UnusedHttpClientFactory(),
                new UnusedCurseForgeClient(),
                configuration,
                NullLogger<PackImporter>.Instance);

            await importer.RunAsync(import.Id, CancellationToken.None);

            var row = await db.Mods.SingleAsync();
            await Assert.That(row.FileName).IsEqualTo("jei.jar");
            await Assert.That(row.ModIds).IsEquivalentTo(new[] { "jei" });
        }

        private sealed class UnusedHttpClientFactory : IHttpClientFactory
        {
            public HttpClient CreateClient(string name) =>
                throw new NotSupportedException("A zip of jars needs no downloads.");
        }

        private sealed class UnusedCurseForgeClient : ICurseForgeClient
        {
            public bool IsConfigured => false;

            public Task<IReadOnlyDictionary<int, CurseForgeFile>> ResolveAsync(
                IReadOnlyList<int> fileIds, CancellationToken cancellationToken) =>
                throw new NotSupportedException("A zip of jars has no CurseForge manifest.");

            public Task<Uri?> FindOnModrinthBySha1Async(string sha1, CancellationToken cancellationToken) =>
                throw new NotSupportedException("A zip of jars has no CurseForge manifest.");
        }

        [Test]
        public async Task ResolvePendingMod_StoresModIds()
        {
            using var dir = new TempDir();
            await using var db = NewDb();
            var serverId = Guid.NewGuid();

            var pending = new PendingMod
            {
                ServerId = serverId,
                ImportId = Guid.NewGuid(),
                FileName = "jei.jar",
                Reason = PendingReason.Blocked,
            };

            db.PendingMods.Add(pending);
            await db.SaveChangesAsync();

            var handler = new ResolvePendingModCommandHandler(db, StorageIn(dir.Path), new StubUser("alex"));

            await handler.Handle(
                new ResolvePendingModCommand(serverId, pending.Id, "jei.jar", new MemoryStream(ForgeJar("jei"))),
                CancellationToken.None);

            var row = await db.Mods.SingleAsync();
            await Assert.That(row.ModIds).IsEquivalentTo(new[] { "jei" });
        }

        private sealed class ModrinthFixture : IDisposable
        {
            public TempDir Dir { get; } = new();
            public HopperDbContext Db { get; } = NewDb();
            public FakeModrinthClient Client { get; } = new();
            public FileSystemBlobStorage Blobs { get; }
            public Guid ServerId { get; } = Guid.NewGuid();

            public ModrinthFixture()
            {
                Blobs = StorageIn(Dir.Path);

                Db.Servers.Add(new Server
                {
                    Id = ServerId,
                    Name = "Test",
                    Slug = "test",
                    Token = new string('a', 64),
                    MinecraftVersion = "1.20.1",
                    Loader = ModLoader.Forge,
                    LoaderVersion = "47.4.10",
                });

                Db.SaveChanges();
            }

            public InstallModrinthModsCommandHandler Handler() =>
                new(Db, Blobs, Client, new StubUser("alex"));

            public void Dispose()
            {
                Db.Dispose();
                Dir.Dispose();
            }
        }

        [Test]
        public async Task ModrinthInstall_StoresModIds()
        {
            using var fixture = new ModrinthFixture();
            fixture.Client.AddDownloadableMod("u6dRKJwZ", "mcC2LhSG", "Just Enough Items", "jei.jar", ForgeJar("jei"));

            await fixture.Handler().Handle(
                new InstallModrinthModsCommand(fixture.ServerId, [new ModrinthInstallItem("mcC2LhSG", false)]),
                CancellationToken.None);

            var row = await fixture.Db.Mods.SingleAsync();
            await Assert.That(row.ModIds).IsEquivalentTo(new[] { "jei" });
        }

        [Test]
        public async Task ModrinthAdopt_BackfillsModIdsOnALegacyRowThatHadNone()
        {
            using var fixture = new ModrinthFixture();
            var bytes = ForgeJar("jei");

            fixture.Db.Mods.Add(new Mod
            {
                ServerId = fixture.ServerId,
                FileName = "jei-hand-uploaded.jar",
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                Size = bytes.Length,
                ModIds = null,
            });

            await fixture.Db.SaveChangesAsync();
            await using (var stream = new MemoryStream(bytes))
                await fixture.Blobs.SaveAsync(stream, CancellationToken.None);

            fixture.Client.AddDownloadableMod("u6dRKJwZ", "mcC2LhSG", "Just Enough Items", "jei.jar", bytes);

            var result = await fixture.Handler().Handle(
                new InstallModrinthModsCommand(fixture.ServerId, [new ModrinthInstallItem("mcC2LhSG", false)]),
                CancellationToken.None);

            await Assert.That(result.Adopted).Count().IsEqualTo(1);

            var row = await fixture.Db.Mods.SingleAsync();
            await Assert.That(row.FileName).IsEqualTo("jei-hand-uploaded.jar");
            await Assert.That(row.ModIds).IsEquivalentTo(new[] { "jei" });
        }

        [Test]
        public async Task ModrinthAdopt_DoesNotOverwriteModIdsThatWereAlreadyRead()
        {
            using var fixture = new ModrinthFixture();
            var bytes = ForgeJar("jei");

            fixture.Db.Mods.Add(new Mod
            {
                ServerId = fixture.ServerId,
                FileName = "jei-hand-uploaded.jar",
                Sha256 = Convert.ToHexStringLower(SHA256.HashData(bytes)),
                Size = bytes.Length,
                ModIds = ["alreadyread"],
            });

            await fixture.Db.SaveChangesAsync();
            await using (var stream = new MemoryStream(bytes))
                await fixture.Blobs.SaveAsync(stream, CancellationToken.None);

            fixture.Client.AddDownloadableMod("u6dRKJwZ", "mcC2LhSG", "Just Enough Items", "jei.jar", bytes);

            await fixture.Handler().Handle(
                new InstallModrinthModsCommand(fixture.ServerId, [new ModrinthInstallItem("mcC2LhSG", false)]),
                CancellationToken.None);

            var row = await fixture.Db.Mods.SingleAsync();
            await Assert.That(row.ModIds).IsEquivalentTo(new[] { "alreadyread" });
        }
    }
}
