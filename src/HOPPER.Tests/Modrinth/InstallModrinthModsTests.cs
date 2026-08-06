using System.Security.Cryptography;
using System.Text;
using HOPPER.Application.Command.Modrinth;
using HOPPER.Application.Dtos.Modrinth;
using HOPPER.Application.Modrinth;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Tests.Modrinth
{
    /// <summary>
    /// Install is the only part of this feature that writes anything, so the cases that matter are the
    /// ones where a wrong write is hard to undo: a jar stored under a hash that does not describe it,
    /// a blob left behind after a failed verification, a second row for bytes that are already here,
    /// or an incompatible set landing because the plan the admin confirmed had gone stale.
    ///
    /// It also pins the shape of the two-phase design: install resolves NOTHING. Handing it one
    /// version id installs one mod, even when that mod declares required dependencies - because those
    /// dependencies were listed in the plan and arrive as their own items.
    ///
    /// Everything is driven by FakeModrinthClient. Nothing here touches the live API.
    /// </summary>
    public class InstallModrinthModsTests
    {
        private sealed class TempDir : IDisposable
        {
            public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hopper-install-" + Guid.NewGuid().ToString("N"));
            public TempDir() => Directory.CreateDirectory(Path);
            public void Dispose() { try { Directory.Delete(Path, recursive: true); } catch { } }
        }

        private sealed class StubUser(string? name) : ICurrentUserService
        {
            public string? Name { get; } = name;
        }

        private sealed class Fixture : IDisposable
        {
            public TempDir Dir { get; } = new();
            public HopperDbContext Db { get; }
            public FileSystemBlobStorage Blobs { get; }
            public FakeModrinthClient Client { get; } = new();
            public Guid ServerId { get; } = Guid.NewGuid();

            public Fixture()
            {
                Blobs = new FileSystemBlobStorage(new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["Blobs:Directory"] = Dir.Path })
                    .Build());

                Db = new HopperDbContext(new DbContextOptionsBuilder<HopperDbContext>()
                    .UseInMemoryDatabase($"hopper-install-{Guid.NewGuid():N}")
                    .Options);

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

            public async Task<Dtos> RunAsync(params ModrinthInstallItem[] items) =>
                new(await Handler().Handle(new InstallModrinthModsCommand(ServerId, items), CancellationToken.None));

            public void Dispose()
            {
                Db.Dispose();
                Dir.Dispose();
            }
        }

        /// <summary>Thin alias so the assertions below read as prose rather than as a long generic.</summary>
        private sealed record Dtos(ModrinthInstallResultDto Result)
        {
            public int Installed => Result.Installed.Count;
            public int Skipped => Result.Skipped.Count;
            public int Failed => Result.Failed.Count;
            public int Adopted => Result.Adopted.Count;
            public int Replaced => Result.Replaced.Count;
        }

        private static byte[] Jar(string marker) => Encoding.UTF8.GetBytes($"PK jar {marker}");

        private static string Sha256Of(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

        // ---- the happy path ----------------------------------------------------------------

        [Test]
        public async Task Install_GoodJar_WritesARowWithEveryProvenanceFieldAndAComputedSha256()
        {
            // Modrinth never publishes sha256, so the blob address here is one HOPPER computed from
            // the bytes it actually received. That is the whole reason the download happens
            // server-side.
            using var fixture = new Fixture();
            var bytes = Jar("jei");
            fixture.Client.AddDownloadableMod("u6dRKJwZ", "mcC2LhSG", "Just Enough Items", "jei.jar", bytes);

            var result = await fixture.RunAsync(new ModrinthInstallItem("mcC2LhSG", false));

            await Assert.That(result.Installed).IsEqualTo(1);

            var row = await fixture.Db.Mods.SingleAsync();
            await Assert.That(row.FileName).IsEqualTo("jei.jar");
            await Assert.That(row.Sha256).IsEqualTo(Sha256Of(bytes));
            await Assert.That(row.Size).IsEqualTo((long)bytes.Length);
            await Assert.That(row.Source).IsEqualTo(ModSource.Modrinth);
            await Assert.That(row.ProjectId).IsEqualTo("u6dRKJwZ");
            await Assert.That(row.VersionId).IsEqualTo("mcC2LhSG");
            await Assert.That(row.ProjectName).IsEqualTo("Just Enough Items");
            await Assert.That(row.DownloadUrl).StartsWith("https://cdn.modrinth.com/");
            await Assert.That(row.Sha1).IsEqualTo(Convert.ToHexStringLower(SHA1.HashData(bytes)));
            await Assert.That(row.Sha512).IsEqualTo(Convert.ToHexStringLower(SHA512.HashData(bytes)));
            await Assert.That(row.UploadedBy).IsEqualTo("alex");

            // And the row passes the check the exporters actually make.
            await Assert.That(row.HasModrinthProvenance()).IsTrue();
            await Assert.That(fixture.Blobs.Exists(row.Sha256)).IsTrue();
        }

        [Test]
        public async Task Install_ResolvesNothing_EvenWhenTheModDeclaresRequiredDependencies()
        {
            // The guarantee behind the two-phase design. A dependency reaches the database only by
            // having been in the plan and arriving as its own item.
            using var fixture = new Fixture();
            fixture.Client.AddDownloadableMod("PA", "v-a", "A", "a.jar", Jar("a"),
                dependencies: FakeModrinthClient.Required("PB"));
            fixture.Client.AddDownloadableMod("PB", "v-b", "B", "b.jar", Jar("b"));

            await fixture.RunAsync(new ModrinthInstallItem("v-a", false));

            await Assert.That(await fixture.Db.Mods.CountAsync()).IsEqualTo(1);
            await Assert.That((await fixture.Db.Mods.SingleAsync()).FileName).IsEqualTo("a.jar");
        }

        [Test]
        public async Task Install_SeveralMods_CostsOneBulkLookupNotOnePerMod()
        {
            using var fixture = new Fixture();
            fixture.Client.AddDownloadableMod("PA", "v-a", "A", "a.jar", Jar("a"));
            fixture.Client.AddDownloadableMod("PB", "v-b", "B", "b.jar", Jar("b"));
            fixture.Client.AddDownloadableMod("PC", "v-c", "C", "c.jar", Jar("c"));

            var result = await fixture.RunAsync(
                new ModrinthInstallItem("v-a", false),
                new ModrinthInstallItem("v-b", false),
                new ModrinthInstallItem("v-c", false));

            await Assert.That(result.Installed).IsEqualTo(3);
            await Assert.That(fixture.Client.Calls.Count(c => c.StartsWith("versions:"))).IsEqualTo(1);
        }

        // ---- verification ------------------------------------------------------------------

        [Test]
        public async Task Install_HashMismatch_FailsThatItemAndLeavesNoRowAndNoBlob()
        {
            // The bytes are not what Modrinth said they would be. Storing them anyway would mean every
            // client on this server downloading a jar nobody vouched for.
            using var fixture = new Fixture();
            var bytes = Jar("tampered");

            fixture.Client.AddDownloadableMod(
                "PA", "v-a", "A", "a.jar", bytes,
                publishedSha512: new string('0', 128));

            var result = await fixture.RunAsync(new ModrinthInstallItem("v-a", false));

            await Assert.That(result.Installed).IsEqualTo(0);
            await Assert.That(result.Failed).IsEqualTo(1);
            await Assert.That(result.Result.Failed.Single().Error).Contains("does not match");

            await Assert.That(await fixture.Db.Mods.AnyAsync()).IsFalse();

            // Orphan-collected: the blob was written before it could be checked, so it has to go back.
            await Assert.That(fixture.Blobs.Exists(Sha256Of(bytes))).IsFalse();
        }

        [Test]
        public async Task Install_HashMismatch_DoesNotCollectABlobAnotherModStillUses()
        {
            // The orphan check is global and runs before Delete. A shared blob must survive a failed
            // verification of a different mod that happened to produce the same bytes.
            using var fixture = new Fixture();
            var bytes = Jar("shared");
            var (sha256, size) = await fixture.Blobs.SaveAsync(new MemoryStream(bytes));

            fixture.Db.Mods.Add(new Mod
            {
                ServerId = Guid.NewGuid(), // another server entirely
                FileName = "elsewhere.jar",
                Sha256 = sha256,
                Size = size,
            });
            await fixture.Db.SaveChangesAsync();

            fixture.Client.AddDownloadableMod("PA", "v-a", "A", "a.jar", bytes, publishedSha1: new string('0', 40));

            await fixture.RunAsync(new ModrinthInstallItem("v-a", false));

            await Assert.That(fixture.Blobs.Exists(sha256)).IsTrue();
        }

        [Test]
        public async Task Install_ModrinthPublishingNoHashes_IsRefusedRatherThanTrusted()
        {
            using var fixture = new Fixture();
            var version = fixture.Client.AddMod("PA", "v-a", "A", "a.jar");

            fixture.Client.Versions["v-a"] = version with
            {
                Files = [version.Files[0] with { Hashes = new Dictionary<string, string>() }],
            };

            var result = await fixture.RunAsync(new ModrinthInstallItem("v-a", false));

            await Assert.That(result.Failed).IsEqualTo(1);
            await Assert.That(result.Result.Failed.Single().Error).Contains("cannot be verified");
            await Assert.That(await fixture.Db.Mods.AnyAsync()).IsFalse();
        }

        [Test]
        public async Task Install_OneFailingItem_DoesNotAbortTheBatch()
        {
            using var fixture = new Fixture();
            fixture.Client.AddDownloadableMod("PA", "v-a", "A", "a.jar", Jar("a"));
            fixture.Client.AddDownloadableMod("PB", "v-b", "B", "b.jar", Jar("b"), publishedSha1: new string('0', 40));
            fixture.Client.AddDownloadableMod("PC", "v-c", "C", "c.jar", Jar("c"));

            var result = await fixture.RunAsync(
                new ModrinthInstallItem("v-a", false),
                new ModrinthInstallItem("v-b", false),
                new ModrinthInstallItem("v-c", false));

            await Assert.That(result.Installed).IsEqualTo(2);
            await Assert.That(result.Failed).IsEqualTo(1);
            await Assert.That(await fixture.Db.Mods.CountAsync()).IsEqualTo(2);
        }

        [Test]
        public async Task Install_DownloadThatFails_IsAPerItemFailure()
        {
            using var fixture = new Fixture();
            var version = fixture.Client.AddDownloadableMod("PA", "v-a", "A", "a.jar", Jar("a"));
            fixture.Client.FailingDownloads.Add(version.Files[0].Url!);
            fixture.Client.AddDownloadableMod("PB", "v-b", "B", "b.jar", Jar("b"));

            var result = await fixture.RunAsync(
                new ModrinthInstallItem("v-a", false),
                new ModrinthInstallItem("v-b", false));

            await Assert.That(result.Installed).IsEqualTo(1);
            await Assert.That(result.Failed).IsEqualTo(1);
        }

        [Test]
        public async Task Install_VersionModrinthNoLongerHas_IsReportedRatherThanSilentlyDropped()
        {
            using var fixture = new Fixture();
            fixture.Client.AddDownloadableMod("PA", "v-a", "A", "a.jar", Jar("a"));

            var result = await fixture.RunAsync(
                new ModrinthInstallItem("v-a", false),
                new ModrinthInstallItem("v-vanished", false));

            await Assert.That(result.Installed).IsEqualTo(1);
            await Assert.That(result.Failed).IsEqualTo(1);
            await Assert.That(result.Result.Failed.Single().Name).IsEqualTo("v-vanished");
        }

        [Test]
        public async Task Install_NonJarFileName_IsRefused()
        {
            // The same rule the client's own sanitiser enforces. A manifest entry the client would
            // reject is a silent partial sync, which is far harder to diagnose than a failure here.
            using var fixture = new Fixture();
            fixture.Client.AddDownloadableMod("PA", "v-a", "A", "../evil.zip", Jar("a"));

            var result = await fixture.RunAsync(new ModrinthInstallItem("v-a", false));

            await Assert.That(result.Failed).IsEqualTo(1);
            await Assert.That(await fixture.Db.Mods.AnyAsync()).IsFalse();
        }

        // ---- against what is already installed ----------------------------------------------

        [Test]
        public async Task Install_ExactVersionAlreadyThere_IsSkipped()
        {
            using var fixture = new Fixture();
            fixture.Client.AddDownloadableMod("PA", "v-a", "A", "a.jar", Jar("a"));

            await fixture.RunAsync(new ModrinthInstallItem("v-a", false));
            var second = await fixture.RunAsync(new ModrinthInstallItem("v-a", false));

            await Assert.That(second.Installed).IsEqualTo(0);
            await Assert.That(second.Skipped).IsEqualTo(1);
            await Assert.That(await fixture.Db.Mods.CountAsync()).IsEqualTo(1);
        }

        [Test]
        public async Task Install_OtherVersionOfTheSameProject_IsSkippedUnlessReplaceIsTicked()
        {
            // Defaults to skip. An upgrade is a deliberate act, and (ServerId, FileName) is unique so a
            // blind insert would conflict anyway.
            using var fixture = new Fixture();
            fixture.Client.AddDownloadableMod("PA", "v-old", "A", "a-old.jar", Jar("old"));
            fixture.Client.AddDownloadableMod("PA", "v-new", "A", "a-new.jar", Jar("new"));

            await fixture.RunAsync(new ModrinthInstallItem("v-old", false));

            var skipped = await fixture.RunAsync(new ModrinthInstallItem("v-new", false));
            await Assert.That(skipped.Skipped).IsEqualTo(1);
            await Assert.That((await fixture.Db.Mods.SingleAsync()).VersionId).IsEqualTo("v-old");
        }

        [Test]
        public async Task Install_WithReplace_SwapsTheRowAndCollectsTheOldBlob()
        {
            using var fixture = new Fixture();
            var oldBytes = Jar("old");
            fixture.Client.AddDownloadableMod("PA", "v-old", "A", "a-old.jar", oldBytes);
            fixture.Client.AddDownloadableMod("PA", "v-new", "A", "a-new.jar", Jar("new"));

            await fixture.RunAsync(new ModrinthInstallItem("v-old", false));
            var result = await fixture.RunAsync(new ModrinthInstallItem("v-new", true));

            await Assert.That(result.Installed).IsEqualTo(1);
            await Assert.That(result.Replaced).IsEqualTo(1);

            var row = await fixture.Db.Mods.SingleAsync();
            await Assert.That(row.VersionId).IsEqualTo("v-new");
            await Assert.That(row.FileName).IsEqualTo("a-new.jar");

            // Nothing references the old bytes any more, so they are gone.
            await Assert.That(fixture.Blobs.Exists(Sha256Of(oldBytes))).IsFalse();
        }

        [Test]
        public async Task Install_FileNameTakenByAHandUploadedJar_IsSkippedUnlessReplaceIsTicked()
        {
            using var fixture = new Fixture();
            var (sha256, size) = await fixture.Blobs.SaveAsync(new MemoryStream(Jar("manual")));

            fixture.Db.Mods.Add(new Mod
            {
                ServerId = fixture.ServerId,
                FileName = "jei.jar",
                Sha256 = sha256,
                Size = size,
            });
            await fixture.Db.SaveChangesAsync();

            fixture.Client.AddDownloadableMod("u6dRKJwZ", "mcC2LhSG", "JEI", "jei.jar", Jar("jei"));

            var result = await fixture.RunAsync(new ModrinthInstallItem("mcC2LhSG", false));

            await Assert.That(result.Skipped).IsEqualTo(1);
            await Assert.That(result.Result.Skipped.Single().Reason).Contains("already on this server");
            await Assert.That((await fixture.Db.Mods.SingleAsync()).Source).IsEqualTo(ModSource.Manual);
        }

        // ---- the same bytes under another name ----------------------------------------------

        [Test]
        public async Task Install_SameBytesAsAHandUploadedJar_AdoptsTheRowInsteadOfDuplicatingIt()
        {
            // Modrinth never publishes sha256, so the plan could not possibly have known - this is only
            // detectable after the download. A second row would make the client write the identical jar
            // twice under two names, which Forge may refuse outright.
            using var fixture = new Fixture();
            var bytes = Jar("identical");
            var (sha256, size) = await fixture.Blobs.SaveAsync(new MemoryStream(bytes));

            fixture.Db.Mods.Add(new Mod
            {
                ServerId = fixture.ServerId,
                FileName = "jei-renamed-by-hand.jar",
                Sha256 = sha256,
                Size = size,
                UploadedBy = "someone",
            });
            await fixture.Db.SaveChangesAsync();

            fixture.Client.AddDownloadableMod("u6dRKJwZ", "mcC2LhSG", "Just Enough Items", "jei.jar", bytes);

            var result = await fixture.RunAsync(new ModrinthInstallItem("mcC2LhSG", false));

            await Assert.That(result.Adopted).IsEqualTo(1);
            await Assert.That(result.Installed).IsEqualTo(0);
            await Assert.That(await fixture.Db.Mods.CountAsync()).IsEqualTo(1);

            var row = await fixture.Db.Mods.SingleAsync();

            // The filename is KEPT: it is what the clients already hold on disk.
            await Assert.That(row.FileName).IsEqualTo("jei-renamed-by-hand.jar");

            // And it now exports with a real CDN URL rather than as an override.
            await Assert.That(row.Source).IsEqualTo(ModSource.Modrinth);
            await Assert.That(row.ProjectId).IsEqualTo("u6dRKJwZ");
            await Assert.That(row.HasModrinthProvenance()).IsTrue();
        }

        [Test]
        public async Task Install_SameBytesAsAnAlreadyTrackedModrinthJar_IsSkipped()
        {
            using var fixture = new Fixture();
            var bytes = Jar("identical");

            fixture.Client.AddDownloadableMod("PA", "v-a", "A", "a.jar", bytes);
            fixture.Client.AddDownloadableMod("PB", "v-b", "B", "b.jar", bytes);

            await fixture.RunAsync(new ModrinthInstallItem("v-a", false));
            var result = await fixture.RunAsync(new ModrinthInstallItem("v-b", false));

            await Assert.That(result.Skipped).IsEqualTo(1);
            await Assert.That(result.Result.Skipped.Single().Reason).Contains("a.jar");
            await Assert.That(await fixture.Db.Mods.CountAsync()).IsEqualTo(1);
        }

        // ---- refusal -------------------------------------------------------------------------

        [Test]
        public async Task Install_SetIncompatibleWithSomethingOnTheServer_ThrowsAndWritesNothing()
        {
            // Re-checked here rather than trusted from the plan: the dialog may be minutes old. And it
            // throws before a single byte is downloaded, so the refusal is total.
            using var fixture = new Fixture();

            fixture.Db.Mods.Add(new Mod
            {
                ServerId = fixture.ServerId,
                FileName = "rubidium.jar",
                Sha256 = new string('c', 64),
                Size = 1,
                Source = ModSource.Modrinth,
                ProjectId = "PRUBIDIUM",
                VersionId = "v-rubidium",
            });
            await fixture.Db.SaveChangesAsync();

            fixture.Client.AddDownloadableMod("PEMBEDDIUM", "v-embeddium", "Embeddium", "embeddium.jar", Jar("e"),
                dependencies: FakeModrinthClient.Incompatible("PRUBIDIUM"));

            await Assert.That(async () => await fixture.RunAsync(new ModrinthInstallItem("v-embeddium", false)))
                .Throws<IncompatibleModException>();

            await Assert.That(await fixture.Db.Mods.CountAsync()).IsEqualTo(1);
            await Assert.That(fixture.Client.Calls.Any(c => c.StartsWith("download:"))).IsFalse();
        }

        [Test]
        public async Task Install_TwoMutuallyIncompatibleModsInOneBatch_ThrowsAndWritesNothing()
        {
            using var fixture = new Fixture();
            fixture.Client.AddDownloadableMod("PA", "v-a", "A", "a.jar", Jar("a"),
                dependencies: FakeModrinthClient.Incompatible("PB"));
            fixture.Client.AddDownloadableMod("PB", "v-b", "B", "b.jar", Jar("b"));

            await Assert.That(async () => await fixture.RunAsync(
                    new ModrinthInstallItem("v-a", false),
                    new ModrinthInstallItem("v-b", false)))
                .Throws<IncompatibleModException>();

            await Assert.That(await fixture.Db.Mods.AnyAsync()).IsFalse();
        }

        [Test]
        public async Task Install_IncompatibilityAgainstSomethingAbsent_DoesNotRefuse()
        {
            using var fixture = new Fixture();
            fixture.Client.AddDownloadableMod("PEMBEDDIUM", "v-embeddium", "Embeddium", "embeddium.jar", Jar("e"),
                dependencies: FakeModrinthClient.Incompatible("PRUBIDIUM"));

            var result = await fixture.RunAsync(new ModrinthInstallItem("v-embeddium", false));

            await Assert.That(result.Installed).IsEqualTo(1);
        }

        // ---- input --------------------------------------------------------------------------

        [Test]
        public async Task Install_NothingSelected_IsRefused()
        {
            using var fixture = new Fixture();

            await Assert.That(async () => await fixture.RunAsync())
                .Throws<ArgumentException>();
        }

        [Test]
        public async Task Install_UnknownServer_IsANotFound()
        {
            using var fixture = new Fixture();
            fixture.Client.AddDownloadableMod("PA", "v-a", "A", "a.jar", Jar("a"));

            await Assert.That(async () => await fixture.Handler().Handle(
                    new InstallModrinthModsCommand(Guid.NewGuid(), [new ModrinthInstallItem("v-a", false)]),
                    CancellationToken.None))
                .Throws<HOPPER.Application.ServerNotFoundException>();
        }

        [Test]
        public async Task Install_SameVersionTwiceInOneRequest_InstallsItOnce()
        {
            using var fixture = new Fixture();
            fixture.Client.AddDownloadableMod("PA", "v-a", "A", "a.jar", Jar("a"));

            var result = await fixture.RunAsync(
                new ModrinthInstallItem("v-a", false),
                new ModrinthInstallItem("v-a", false));

            await Assert.That(result.Installed).IsEqualTo(1);
            await Assert.That(await fixture.Db.Mods.CountAsync()).IsEqualTo(1);
        }

        [Test]
        public async Task Install_TwoServers_ShareTheBlobAndKeepTheirOwnRows()
        {
            // Content addressing makes this free, and it is the reason the orphan check has to be
            // global everywhere else.
            using var fixture = new Fixture();
            var otherServer = Guid.NewGuid();

            fixture.Db.Servers.Add(new Server
            {
                Id = otherServer,
                Name = "Other",
                Slug = "other",
                Token = new string('b', 64),
            });
            await fixture.Db.SaveChangesAsync();

            fixture.Client.AddDownloadableMod("PA", "v-a", "A", "a.jar", Jar("a"));

            await fixture.RunAsync(new ModrinthInstallItem("v-a", false));
            await fixture.Handler().Handle(
                new InstallModrinthModsCommand(otherServer, [new ModrinthInstallItem("v-a", false)]),
                CancellationToken.None);

            var rows = await fixture.Db.Mods.ToListAsync();
            await Assert.That(rows.Count).IsEqualTo(2);
            await Assert.That(rows.Select(r => r.Sha256).Distinct().Count()).IsEqualTo(1);
        }
    }
}
