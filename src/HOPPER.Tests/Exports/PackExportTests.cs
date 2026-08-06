using System.IO.Compression;
using System.Text;
using System.Text.Json;
using HOPPER.Application;
using HOPPER.Application.Exports;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Tests.Exports
{
    /// <summary>
    /// The exporters write real files that other people's launchers read, so what is asserted here is
    /// the archive itself - entry names, manifest keys, which jar landed where - and not a return
    /// value.
    ///
    /// The rule the whole feature turns on gets its own test: a mod HOPPER knows the Modrinth origin
    /// of becomes a manifest entry pointing at the real CDN, and everything else ships as bytes. A
    /// HOPPER blob URL needs this server's bearer token, so a pack carrying one would be useless to
    /// whoever it was handed to - PortabilityTest below greps all three archives for one.
    /// </summary>
    public class PackExportTests
    {
        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hopper-export-" + Guid.NewGuid().ToString("N"));
            public TempDir() => Directory.CreateDirectory(Path);
            public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
        }

        /// <summary>One server on Forge 1.20.1 with two Modrinth mods and one hand-uploaded one - the
        /// mix every assertion below needs.</summary>
        private sealed class Fixture : IDisposable
        {
            public TempDir Dir { get; } = new();
            public HopperDbContext Db { get; }
            public FileSystemBlobStorage Blobs { get; }
            public IConfiguration Configuration { get; }
            public Guid ServerId { get; } = Guid.NewGuid();

            public const string JeiBytes = "PK jei bytes";
            public const string CreateBytes = "PK create bytes";
            public const string ManualBytes = "PK hand uploaded bytes";

            public Fixture(bool configurePlatform = true)
            {
                Configuration = new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Blobs:Directory"] = Dir.Path })
                    .Build();

                Blobs = new FileSystemBlobStorage(Configuration);

                Db = new HopperDbContext(new DbContextOptionsBuilder<HopperDbContext>()
                    .UseInMemoryDatabase($"hopper-export-{Guid.NewGuid():N}")
                    .Options);

                Db.Servers.Add(new Server
                {
                    Id = ServerId,
                    Name = "Friday Night SMP",
                    Slug = "friday-night-smp",
                    Token = new string('a', 64),
                    MinecraftVersion = configurePlatform ? "1.20.1" : null,
                    Loader = configurePlatform ? ModLoader.Forge : ModLoader.Unknown,
                    LoaderVersion = configurePlatform ? "47.4.10" : null,
                });

                Add("jei.jar", JeiBytes, ModSource.Modrinth, "u6dRKJwZ", "mcC2LhSG", "Just Enough Items");
                Add("create.jar", CreateBytes, ModSource.Modrinth, "LNytGWDc", "iRckjniU", "Create");
                Add("hand-uploaded.jar", ManualBytes, ModSource.Manual, null, null, null);

                Db.SaveChanges();
            }

            private void Add(string fileName, string content, ModSource source, string? projectId, string? versionId, string? title)
            {
                var bytes = Encoding.UTF8.GetBytes(content);
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
                    ProjectName = title,
                    DownloadUrl = source == ModSource.Modrinth
                        ? $"https://cdn.modrinth.com/data/{projectId}/versions/{versionId}/{fileName}"
                        : null,
                    Sha1 = source == ModSource.Modrinth ? new string('1', 40) : null,
                    Sha512 = source == ModSource.Modrinth ? new string('5', 128) : null,
                });
            }

            public MrpackExporter Mrpack() => new(Db, Blobs, Configuration);

            public CurseForgePackExporter CurseForge() => new(Db, Blobs, Configuration);

            public PrismInstanceExporter Prism() => new(Db, Blobs, Configuration);

            public void Dispose()
            {
                Db.Dispose();
                Dir.Dispose();
            }
        }

        /// <summary>Reads the finished archive fully into memory. Test fixtures are a few hundred
        /// bytes; the exporter itself never does this, which is the point of it streaming to disk.</summary>
        private static async Task<Dictionary<string, byte[]>> EntriesOf(PackExportResult result)
        {
            var buffer = new MemoryStream();
            await using (result.Content)
                await result.Content.CopyToAsync(buffer);

            buffer.Position = 0;
            using var archive = new ZipArchive(buffer, ZipArchiveMode.Read);

            var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (var entry in archive.Entries)
            {
                using var stream = entry.Open();
                using var content = new MemoryStream();
                stream.CopyTo(content);
                entries[entry.FullName] = content.ToArray();
            }

            return entries;
        }

        private static JsonElement JsonOf(Dictionary<string, byte[]> entries, string path) =>
            JsonDocument.Parse(entries[path]).RootElement.Clone();

        // ---- .mrpack -----------------------------------------------------------------------

        [Test]
        public async Task Mrpack_HasItsIndexAtTheRootWithTheFormatVersionConsumersRequire()
        {
            using var fixture = new Fixture();

            var entries = await EntriesOf(await fixture.Mrpack().ExportAsync(fixture.ServerId, CancellationToken.None));
            var index = JsonOf(entries, "modrinth.index.json");

            await Assert.That(index.GetProperty("formatVersion").GetInt32()).IsEqualTo(1);
            await Assert.That(index.GetProperty("game").GetString()).IsEqualTo("minecraft");
            await Assert.That(index.GetProperty("name").GetString()).IsEqualTo("Friday Night SMP");
        }

        [Test]
        public async Task Mrpack_Dependencies_AreExactlyMinecraftAndTheLoader()
        {
            // An unrecognised key here is a hard "Unknown dependency type" in Prism, and the loader
            // version is written bare, with no Minecraft prefix.
            using var fixture = new Fixture();

            var entries = await EntriesOf(await fixture.Mrpack().ExportAsync(fixture.ServerId, CancellationToken.None));
            var dependencies = JsonOf(entries, "modrinth.index.json").GetProperty("dependencies");

            var keys = dependencies.EnumerateObject().Select(p => p.Name).Order().ToList();
            await Assert.That(keys).IsEquivalentTo(new[] { "forge", "minecraft" });
            await Assert.That(dependencies.GetProperty("minecraft").GetString()).IsEqualTo("1.20.1");
            await Assert.That(dependencies.GetProperty("forge").GetString()).IsEqualTo("47.4.10");
        }

        [Test]
        public async Task Mrpack_OnlyModsWithProvenance_BecomeManifestEntries()
        {
            using var fixture = new Fixture();

            var entries = await EntriesOf(await fixture.Mrpack().ExportAsync(fixture.ServerId, CancellationToken.None));
            var files = JsonOf(entries, "modrinth.index.json").GetProperty("files");

            await Assert.That(files.GetArrayLength()).IsEqualTo(2);

            var paths = files.EnumerateArray().Select(f => f.GetProperty("path").GetString()!).Order().ToList();
            await Assert.That(paths).IsEquivalentTo(new[] { "mods/create.jar", "mods/jei.jar" });
        }

        [Test]
        public async Task Mrpack_ManifestEntry_CarriesSha1AndSha512AndNeverSha256()
        {
            // sha256 is not an algorithm this format or any of its consumers know. It is the blob
            // address and it stays there.
            using var fixture = new Fixture();

            var entries = await EntriesOf(await fixture.Mrpack().ExportAsync(fixture.ServerId, CancellationToken.None));
            var files = JsonOf(entries, "modrinth.index.json").GetProperty("files");

            foreach (var file in files.EnumerateArray())
            {
                var hashes = file.GetProperty("hashes");
                var keys = hashes.EnumerateObject().Select(p => p.Name).Order().ToList();

                await Assert.That(keys).IsEquivalentTo(new[] { "sha1", "sha512" });
                await Assert.That(hashes.GetProperty("sha1").GetString()!.Length).IsEqualTo(40);
                await Assert.That(hashes.GetProperty("sha512").GetString()!.Length).IsEqualTo(128);
            }
        }

        [Test]
        public async Task Mrpack_ManifestEntry_PointsAtTheRealCdnAndCarriesTheSize()
        {
            using var fixture = new Fixture();

            var entries = await EntriesOf(await fixture.Mrpack().ExportAsync(fixture.ServerId, CancellationToken.None));
            var files = JsonOf(entries, "modrinth.index.json").GetProperty("files");

            foreach (var file in files.EnumerateArray())
            {
                var downloads = file.GetProperty("downloads");
                await Assert.That(downloads.GetArrayLength()).IsEqualTo(1);
                await Assert.That(downloads[0].GetString()).StartsWith("https://cdn.modrinth.com/");
                await Assert.That(file.GetProperty("fileSize").GetInt64()).IsGreaterThan(0L);

                var env = file.GetProperty("env");
                await Assert.That(env.GetProperty("client").GetString()).IsEqualTo("required");
                await Assert.That(env.GetProperty("server").GetString()).IsEqualTo("required");
            }
        }

        [Test]
        public async Task Mrpack_TheHandUploadedJar_ShipsInOverridesAsBytes()
        {
            using var fixture = new Fixture();

            var entries = await EntriesOf(await fixture.Mrpack().ExportAsync(fixture.ServerId, CancellationToken.None));

            await Assert.That(entries.ContainsKey("overrides/mods/hand-uploaded.jar")).IsTrue();
            await Assert.That(Encoding.UTF8.GetString(entries["overrides/mods/hand-uploaded.jar"]))
                .IsEqualTo(Fixture.ManualBytes);

            // And the two that are in files[] are NOT also shipped as bytes: they would be downloaded
            // and then overwritten by themselves.
            await Assert.That(entries.ContainsKey("overrides/mods/jei.jar")).IsFalse();
        }

        [Test]
        public async Task Mrpack_IsNamedForTheServerAndTheMoment()
        {
            using var fixture = new Fixture();

            var result = await fixture.Mrpack().ExportAsync(fixture.ServerId, CancellationToken.None);
            await using var _ = result.Content;

            await Assert.That(result.FileName).StartsWith("friday-night-smp-");
            await Assert.That(result.FileName).EndsWith(".mrpack");
            await Assert.That(result.ContentType).IsEqualTo("application/x-modrinth-modpack+zip");
        }

        // ---- CurseForge --------------------------------------------------------------------

        [Test]
        public async Task CurseForge_Manifest_HasTheTypeAndVersionConsumersRequire()
        {
            using var fixture = new Fixture();

            var entries = await EntriesOf(await fixture.CurseForge().ExportAsync(fixture.ServerId, CancellationToken.None));
            var manifest = JsonOf(entries, "manifest.json");

            await Assert.That(manifest.GetProperty("manifestType").GetString()).IsEqualTo("minecraftModpack");
            await Assert.That(manifest.GetProperty("manifestVersion").GetInt32()).IsEqualTo(1);
            await Assert.That(manifest.GetProperty("overrides").GetString()).IsEqualTo("overrides");
            await Assert.That(manifest.GetProperty("name").GetString()).IsEqualTo("Friday Night SMP");
        }

        [Test]
        public async Task CurseForge_ModLoaderId_IsTheBareLoaderBuildWithNoMinecraftPrefix()
        {
            // Consumers strip "forge-" and take the rest verbatim, so "forge-1.20.1-47.4.10" would
            // become a loader version of "1.20.1-47.4.10" and resolve to nothing.
            using var fixture = new Fixture();

            var entries = await EntriesOf(await fixture.CurseForge().ExportAsync(fixture.ServerId, CancellationToken.None));
            var minecraft = JsonOf(entries, "manifest.json").GetProperty("minecraft");

            await Assert.That(minecraft.GetProperty("version").GetString()).IsEqualTo("1.20.1");

            var loader = minecraft.GetProperty("modLoaders")[0];
            await Assert.That(loader.GetProperty("id").GetString()).IsEqualTo("forge-47.4.10");
            await Assert.That(loader.GetProperty("primary").GetBoolean()).IsTrue();
        }

        [Test]
        public async Task CurseForge_Files_IsEmptyAndEveryJarShipsInline()
        {
            // A CurseForge files[] entry is two integers - a CurseForge project id and file id. HOPPER
            // has neither for a Modrinth-sourced or hand-uploaded mod and cannot invent them, so the
            // whole set ships in overrides/mods/. That is a valid, importable pack.
            using var fixture = new Fixture();

            var entries = await EntriesOf(await fixture.CurseForge().ExportAsync(fixture.ServerId, CancellationToken.None));

            await Assert.That(JsonOf(entries, "manifest.json").GetProperty("files").GetArrayLength()).IsEqualTo(0);

            await Assert.That(entries.ContainsKey("overrides/mods/jei.jar")).IsTrue();
            await Assert.That(entries.ContainsKey("overrides/mods/create.jar")).IsTrue();
            await Assert.That(entries.ContainsKey("overrides/mods/hand-uploaded.jar")).IsTrue();
            await Assert.That(Encoding.UTF8.GetString(entries["overrides/mods/jei.jar"])).IsEqualTo(Fixture.JeiBytes);
        }

        [Test]
        public async Task CurseForge_ModListNamesEveryModAndLinksTheOnesItCan()
        {
            using var fixture = new Fixture();

            var entries = await EntriesOf(await fixture.CurseForge().ExportAsync(fixture.ServerId, CancellationToken.None));
            var html = Encoding.UTF8.GetString(entries["modlist.html"]);

            await Assert.That(html).Contains("Just Enough Items");
            await Assert.That(html).Contains("https://modrinth.com/mod/u6dRKJwZ");

            // No project name and no link for the hand-uploaded one, so it falls back to its filename.
            await Assert.That(html).Contains("hand-uploaded.jar");
        }

        // ---- Prism instance ----------------------------------------------------------------

        [Test]
        public async Task Prism_InstanceCfg_CarriesTheOneKeyPrismActuallyChecks()
        {
            // InstanceType is load-bearing: Prism rejects the instance outright if it is present and
            // not "OneSix".
            using var fixture = new Fixture();

            var entries = await EntriesOf(await fixture.Prism().ExportAsync(fixture.ServerId, CancellationToken.None));
            var cfg = Encoding.UTF8.GetString(entries["instance.cfg"]);

            await Assert.That(cfg).StartsWith("[General]");
            await Assert.That(cfg).Contains("InstanceType=OneSix");
            await Assert.That(cfg).Contains("name=Friday Night SMP");
        }

        [Test]
        public async Task Prism_MmcPack_HasMinecraftAndTheLoaderAtTheServersVersions()
        {
            using var fixture = new Fixture();

            var entries = await EntriesOf(await fixture.Prism().ExportAsync(fixture.ServerId, CancellationToken.None));
            var pack = JsonOf(entries, "mmc-pack.json");

            await Assert.That(pack.GetProperty("formatVersion").GetInt32()).IsEqualTo(1);

            var components = pack.GetProperty("components").EnumerateArray().ToList();
            await Assert.That(components.Count).IsEqualTo(2);

            var minecraft = components.Single(c => c.GetProperty("uid").GetString() == "net.minecraft");
            await Assert.That(minecraft.GetProperty("version").GetString()).IsEqualTo("1.20.1");
            await Assert.That(minecraft.GetProperty("important").GetBoolean()).IsTrue();

            var forge = components.Single(c => c.GetProperty("uid").GetString() == "net.minecraftforge");
            await Assert.That(forge.GetProperty("version").GetString()).IsEqualTo("47.4.10");
        }

        [Test]
        public async Task Prism_PutsEveryJarInAMaterialisedGameDirectory()
        {
            // An instance is a game directory, not a manifest - there is nothing here to carry a
            // download link, so even the Modrinth mods go in as bytes.
            using var fixture = new Fixture();

            var entries = await EntriesOf(await fixture.Prism().ExportAsync(fixture.ServerId, CancellationToken.None));

            await Assert.That(entries.ContainsKey("minecraft/mods/jei.jar")).IsTrue();
            await Assert.That(entries.ContainsKey("minecraft/mods/create.jar")).IsTrue();
            await Assert.That(entries.ContainsKey("minecraft/mods/hand-uploaded.jar")).IsTrue();
            await Assert.That(Encoding.UTF8.GetString(entries["minecraft/mods/create.jar"])).IsEqualTo(Fixture.CreateBytes);
        }

        [Test]
        public async Task Prism_MustNotContainAModrinthIndex()
        {
            // Prism's zip detection ranks modrinth.index.json ABOVE instance.cfg, so an instance zip
            // carrying one is imported as a Modrinth pack and the instance.cfg is ignored entirely.
            // "A Prism instance wrapping an mrpack" is not a thing - the .mrpack already is that.
            using var fixture = new Fixture();

            var entries = await EntriesOf(await fixture.Prism().ExportAsync(fixture.ServerId, CancellationToken.None));

            await Assert.That(entries.Keys.Any(k => k.EndsWith("modrinth.index.json", StringComparison.Ordinal))).IsFalse();
            await Assert.That(entries.Keys.Any(k => k.EndsWith("manifest.json", StringComparison.Ordinal))).IsFalse();
        }

        [Test]
        public async Task Prism_UsesMinecraftNotDotMinecraft()
        {
            // What Prism creates on Windows, and what PrismPlanner prefers reading back - which is
            // what makes HOPPER's own export re-importable into HOPPER.
            using var fixture = new Fixture();

            var entries = await EntriesOf(await fixture.Prism().ExportAsync(fixture.ServerId, CancellationToken.None));

            await Assert.That(entries.Keys.Any(k => k.StartsWith(".minecraft/", StringComparison.Ordinal))).IsFalse();
        }

        // ---- portability, the rule the whole feature exists for -----------------------------

        [Test]
        public async Task NoExportedPack_ContainsAHopperUrlOrBlobPath()
        {
            // A HOPPER blob URL is reachable only by a client holding this server's token, so a pack
            // carrying one is useless to whoever it is handed to - and Modrinth would refuse to accept
            // it, their whitelist being cdn.modrinth.com, github.com, raw.githubusercontent.com and
            // gitlab.com. This is asserted over the WHOLE archive, text entries and jar bytes alike.
            using var fixture = new Fixture();

            var archives = new[]
            {
                await EntriesOf(await fixture.Mrpack().ExportAsync(fixture.ServerId, CancellationToken.None)),
                await EntriesOf(await fixture.CurseForge().ExportAsync(fixture.ServerId, CancellationToken.None)),
                await EntriesOf(await fixture.Prism().ExportAsync(fixture.ServerId, CancellationToken.None)),
            };

            foreach (var entries in archives)
            {
                foreach (var (path, bytes) in entries)
                {
                    var text = Encoding.UTF8.GetString(bytes);
                    await Assert.That(text).DoesNotContain("/api/blobs/");
                    await Assert.That(text).DoesNotContain("api/manifest");
                    await Assert.That(path).DoesNotContain("..");
                }
            }
        }

        // ---- preconditions -----------------------------------------------------------------

        [Test]
        public async Task Export_ServerWithNoPlatformSet_IsRefusedWithSomethingActionable()
        {
            // All three formats name an exact Minecraft version and loader build. There is nothing
            // sensible to guess, and the message says which fields to fill in.
            using var fixture = new Fixture(configurePlatform: false);

            await Assert.That(async () => await fixture.Mrpack().ExportAsync(fixture.ServerId, CancellationToken.None))
                .Throws<ServerPlatformNotConfiguredException>();

            await Assert.That(async () => await fixture.CurseForge().ExportAsync(fixture.ServerId, CancellationToken.None))
                .Throws<ServerPlatformNotConfiguredException>();

            await Assert.That(async () => await fixture.Prism().ExportAsync(fixture.ServerId, CancellationToken.None))
                .Throws<ServerPlatformNotConfiguredException>();
        }

        [Test]
        public async Task Export_UnknownServer_IsANotFound()
        {
            using var fixture = new Fixture();

            await Assert.That(async () => await fixture.Mrpack().ExportAsync(Guid.NewGuid(), CancellationToken.None))
                .Throws<ServerNotFoundException>();
        }

        [Test]
        public async Task Export_ModWhoseBlobIsGone_IsWarnedAboutRatherThanFailingThePack()
        {
            // An admin with 200 mods and one broken blob wants 199 mods and a note, not a 500.
            using var fixture = new Fixture();

            var orphan = fixture.Db.Mods.First(m => m.FileName == "hand-uploaded.jar");
            fixture.Blobs.Delete(orphan.Sha256);

            var result = await fixture.Mrpack().ExportAsync(fixture.ServerId, CancellationToken.None);
            var entries = await EntriesOf(result);

            await Assert.That(entries.ContainsKey("overrides/mods/hand-uploaded.jar")).IsFalse();
            await Assert.That(result.Warnings.Any(w => w.Contains("hand-uploaded.jar"))).IsTrue();
        }

        [Test]
        public async Task Export_ModClaimingModrinthButMissingItsHashes_DegradesToAnOverride()
        {
            // The exporters test HasModrinthProvenance(), not Source. A half-filled row would otherwise
            // produce a manifest entry with a null download URL, which is an unusable pack; shipping
            // the bytes instead is a correct pack either way.
            using var fixture = new Fixture();

            var broken = fixture.Db.Mods.First(m => m.FileName == "create.jar");
            broken.Sha512 = null;
            await fixture.Db.SaveChangesAsync();

            var entries = await EntriesOf(await fixture.Mrpack().ExportAsync(fixture.ServerId, CancellationToken.None));
            var files = JsonOf(entries, "modrinth.index.json").GetProperty("files");

            await Assert.That(files.GetArrayLength()).IsEqualTo(1);
            await Assert.That(entries.ContainsKey("overrides/mods/create.jar")).IsTrue();
        }

        [Test]
        public async Task Export_ServerWithNoModsAtAll_IsStillAValidPack()
        {
            using var fixture = new Fixture();
            fixture.Db.Mods.RemoveRange(fixture.Db.Mods);
            await fixture.Db.SaveChangesAsync();

            var entries = await EntriesOf(await fixture.Mrpack().ExportAsync(fixture.ServerId, CancellationToken.None));
            var index = JsonOf(entries, "modrinth.index.json");

            await Assert.That(index.GetProperty("files").GetArrayLength()).IsEqualTo(0);
            await Assert.That(index.GetProperty("formatVersion").GetInt32()).IsEqualTo(1);
        }

        // ---- the loader table --------------------------------------------------------------

        [Test]
        public async Task LoaderIds_EveryLoader_HasAllThreeExternalNames()
        {
            // One fact expressed four ways. A drift between them is a pack that imports into one
            // launcher and not another.
            await Assert.That(LoaderIds.MrpackKey(ModLoader.Forge)).IsEqualTo("forge");
            await Assert.That(LoaderIds.MrpackKey(ModLoader.NeoForge)).IsEqualTo("neoforge");
            await Assert.That(LoaderIds.MrpackKey(ModLoader.Fabric)).IsEqualTo("fabric-loader");
            await Assert.That(LoaderIds.MrpackKey(ModLoader.Quilt)).IsEqualTo("quilt-loader");

            await Assert.That(LoaderIds.CurseForgePrefix(ModLoader.Fabric)).IsEqualTo("fabric");
            await Assert.That(LoaderIds.CurseForgePrefix(ModLoader.NeoForge)).IsEqualTo("neoforge");

            await Assert.That(LoaderIds.PrismUid(ModLoader.Forge)).IsEqualTo("net.minecraftforge");
            await Assert.That(LoaderIds.PrismUid(ModLoader.NeoForge)).IsEqualTo("net.neoforged");
            await Assert.That(LoaderIds.PrismUid(ModLoader.Fabric)).IsEqualTo("net.fabricmc.fabric-loader");
            await Assert.That(LoaderIds.PrismUid(ModLoader.Quilt)).IsEqualTo("org.quiltmc.quilt-loader");

            await Assert.That(() => LoaderIds.MrpackKey(ModLoader.Unknown)).Throws<ServerPlatformNotConfiguredException>();
        }
    }
}
