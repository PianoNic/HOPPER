using System.IO.Compression;
using HOPPER.Application.Exports;
using HOPPER.Application.Imports;
using HOPPER.Domain.Enums;

namespace HOPPER.Tests.Imports
{
    public class PackPlanningTests
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

        private static PackPlanContext For(ModLoader loader, string? minecraftVersion) =>
            new() { Target = new PackPlatform(minecraftVersion, loader) };

        private static ZipArchive ArchiveOf(params (string Path, string Content)[] entries) =>
            PackArchive.Of(entries);

        [Test]
        public async Task Detect_MrpackWithADecoyManifestInsideOverrides_IsStillModrinth()
        {
            using var archive = ArchiveOf(
                ("modrinth.index.json", """{"formatVersion":1,"game":"minecraft","files":[]}"""),
                ("overrides/manifest.json", "{}"));

            var detection = PackDetector.Detect(archive);

            await Assert.That(detection.Format).IsEqualTo(PackFormat.Modrinth);
            await Assert.That(detection.Prefix).IsEqualTo(string.Empty);
        }

        [Test]
        public async Task Detect_CurseForgeZip_IsCurseForge()
        {
            using var archive = ArchiveOf(
                ("manifest.json", """{"manifestType":"minecraftModpack","manifestVersion":1,"files":[]}"""),
                ("modlist.html", "<ul></ul>"));

            await Assert.That(PackDetector.Detect(archive).Format).IsEqualTo(PackFormat.CurseForge);
        }

        [Test]
        public async Task Detect_PrismInstanceNestedOneDirectoryDeep_StripsThePrefix()
        {
            using var archive = ArchiveOf(
                ("MyPack/instance.cfg", "[General]\nname=MyPack\n"),
                ("MyPack/mmc-pack.json", "{}"),
                ("MyPack/minecraft/mods/jei.jar", "PK jei"));

            var detection = PackDetector.Detect(archive);

            await Assert.That(detection.Format).IsEqualTo(PackFormat.PrismInstance);
            await Assert.That(detection.Prefix).IsEqualTo("MyPack/");
        }

        [Test]
        public async Task Detect_APrismArchiveHoldingSeveralInstances_SaysSoRatherThanImportingTheFirst()
        {
            using var archive = ArchiveOf(
                ("instances/A/instance.cfg", "[General]\nname=A\n"),
                ("instances/A/minecraft/mods/a.jar", "PK a"),
                ("instances/B/instance.cfg", "[General]\nname=B\n"),
                ("instances/B/minecraft/mods/b.jar", "PK b"));

            var ex = Assert.Throws<PackImportException>(() => PackDetector.Detect(archive));

            await Assert.That(ex!.Message).Contains("2 Prism instances");
            await Assert.That(ex.Message).Contains("instances/A");
            await Assert.That(ex.Message).Contains("instances/B");
        }

        [Test]
        public async Task Detect_PrismZipWrappingAnMrpack_DelegatesToModrinth()
        {
            using var archive = ArchiveOf(
                ("Wrapped/instance.cfg", "[General]\nname=Wrapped\n"),
                ("Wrapped/modrinth.index.json", """{"formatVersion":1,"game":"minecraft","files":[]}"""));

            var detection = PackDetector.Detect(archive);

            await Assert.That(detection.Format).IsEqualTo(PackFormat.Modrinth);
            await Assert.That(detection.Prefix).IsEqualTo("Wrapped/");
        }

        [Test]
        public async Task Detect_TechnicPack_IsRejectedWithAStraightAnswer()
        {
            using var archive = ArchiveOf(("bin/modpack.jar", "PK"), ("bin/version.json", "{}"));

            var exception = await Assert.That(() => PackDetector.Detect(archive)).Throws<PackImportException>();
            await Assert.That(exception!.Message).Contains("Technic");
        }

        [Test]
        public async Task Detect_PlainZipOfJars_IsAJarArchive()
        {
            using var archive = ArchiveOf(("jei.jar", "PK"), ("rei.jar", "PK"));

            await Assert.That(PackDetector.Detect(archive).Format).IsEqualTo(PackFormat.JarArchive);
        }

        [Test]
        public async Task Detect_ZipOfNothingUseful_IsRejected()
        {
            using var archive = ArchiveOf(("notes.txt", "hello"));

            await Assert.That(() => PackDetector.Detect(archive)).Throws<PackImportException>();
        }

        [Test]
        public async Task ModrinthPlan_IndexEntries_CarryTheirUrlsAndHashes()
        {
            using var archive = ArchiveOf(("modrinth.index.json", """
                {"formatVersion":1,"game":"minecraft","versionId":"v1","name":"Test","files":[
                  {"path":"mods/LeavesBeGone.jar",
                   "hashes":{"sha1":"c5043f862be7db76892c7c0c95d02fa3f8332af0","sha512":"7e209ccf"},
                   "env":{"server":"required","client":"required"},
                   "downloads":["https://cdn.modrinth.com/data/AVq17PqV/versions/slScQFdb/LeavesBeGone.jar"],
                   "fileSize":50042}
                ]}
                """));

            var plan = ModrinthPlanner.Plan(archive, string.Empty, PackPlanContext.Default);
            var file = plan.Files.Single();

            await Assert.That(file.FileName).IsEqualTo("LeavesBeGone.jar");
            await Assert.That(file.ZipEntry).IsNull();
            await Assert.That(file.Downloads.Single().Host).IsEqualTo("cdn.modrinth.com");
            await Assert.That(file.Sha512).IsEqualTo("7e209ccf");
            await Assert.That(file.Sha1).IsEqualTo("c5043f862be7db76892c7c0c95d02fa3f8332af0");
            await Assert.That(file.Size).IsEqualTo(50042L);
            await Assert.That(plan.Pending).IsEmpty();
        }

        [Test]
        public async Task ModrinthPlan_OverrideJars_AreIngested()
        {
            using var archive = ArchiveOf(
                ("modrinth.index.json", """{"formatVersion":1,"game":"minecraft","files":[]}"""),
                ("overrides/mods/custom-thing.jar", "PK custom"),
                ("overrides/config/some.toml", "x = 1"),
                ("client-overrides/mods/client-only.jar", "PK client"),
                ("server-overrides/mods/server-only.jar", "PK server"));

            var plan = ModrinthPlanner.Plan(archive, string.Empty, PackPlanContext.Default);

            await Assert.That(plan.Files.Select(f => f.FileName).Order().ToList())
                .IsEquivalentTo(new[] { "client-only.jar", "custom-thing.jar" });
            await Assert.That(plan.Files.All(f => f.ZipEntry is not null)).IsTrue();
        }

        [Test]
        public async Task ModrinthPlan_NonModPaths_AreSkippedNotDropped()
        {
            using var archive = ArchiveOf(("modrinth.index.json", """
                {"formatVersion":1,"game":"minecraft","files":[
                  {"path":"mods/keep.jar","hashes":{"sha1":"a"},"downloads":["https://cdn.modrinth.com/keep.jar"],"fileSize":1},
                  {"path":"resourcepacks/pretty.zip","hashes":{"sha1":"b"},"downloads":["https://cdn.modrinth.com/pretty.zip"],"fileSize":1},
                  {"path":"shaderpacks/fancy.zip","hashes":{"sha1":"c"},"downloads":["https://cdn.modrinth.com/fancy.zip"],"fileSize":1}
                ]}
                """));

            var plan = ModrinthPlanner.Plan(archive, string.Empty, PackPlanContext.Default);

            await Assert.That(plan.Files.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "keep.jar" });
            await Assert.That(plan.Skipped).IsEqualTo(2);
        }

        [Test]
        public async Task ModrinthPlan_ClientUnsupportedEntry_IsSkipped()
        {
            using var archive = ArchiveOf(("modrinth.index.json", """
                {"formatVersion":1,"game":"minecraft","files":[
                  {"path":"mods/server-side.jar","hashes":{"sha1":"a"},"env":{"client":"unsupported","server":"required"},
                   "downloads":["https://cdn.modrinth.com/a.jar"],"fileSize":1},
                  {"path":"mods/no-env.jar","hashes":{"sha1":"b"},"downloads":["https://cdn.modrinth.com/b.jar"],"fileSize":1}
                ]}
                """));

            var plan = ModrinthPlanner.Plan(archive, string.Empty, PackPlanContext.Default);

            await Assert.That(plan.Files.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "no-env.jar" });
        }

        [Test]
        public async Task ModrinthPlan_WrongFormatVersion_IsRejected()
        {
            using var archive = ArchiveOf(("modrinth.index.json", """{"formatVersion":2,"game":"minecraft","files":[]}"""));

            await Assert.That(() => ModrinthPlanner.Plan(archive, string.Empty, PackPlanContext.Default)).Throws<PackImportException>();
        }

        [Test]
        public async Task CurseForgePlan_WithoutAnApiKey_TurnsEveryManifestEntryIntoAPending()
        {
            using var archive = ArchiveOf(
                ("manifest.json", """
                 {"manifestType":"minecraftModpack","manifestVersion":1,"name":"ATM9","version":"1.1.1",
                  "minecraft":{"version":"1.20.1","modLoaders":[{"id":"forge-47.4.0","primary":true}]},
                  "files":[{"projectID":351491,"fileID":6366217,"required":true},
                           {"projectID":514045,"fileID":4938351,"required":true}],
                  "overrides":"overrides"}
                 """),
                ("overrides/mods/cc-tweaked.jar", "PK cc"));

            var plan = await CurseForgePlanner.PlanAsync(archive, string.Empty, PackPlanContext.Default, new KeylessCurseForge(), CancellationToken.None);

            await Assert.That(plan.Files.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "cc-tweaked.jar" });
            await Assert.That(plan.Pending).Count().IsEqualTo(2);
            await Assert.That(plan.Pending.All(p => p.Reason == PendingReason.NoApiKey)).IsTrue();
            await Assert.That(plan.Pending[0].ProjectId).IsEqualTo(351491);
            await Assert.That(plan.Pending[0].FileId).IsEqualTo(6366217);

            await Assert.That(plan.Pending[0].ExpectedSha1).IsNull();
        }

        [Test]
        public async Task CurseForgePlan_OverridesFolderName_IsReadNotAssumed()
        {
            using var archive = ArchiveOf(
                ("manifest.json", """
                 {"manifestType":"minecraftModpack","manifestVersion":1,"files":[],"overrides":"custom"}
                 """),
                ("custom/mods/hidden.jar", "PK hidden"));

            var plan = await CurseForgePlanner.PlanAsync(archive, string.Empty, PackPlanContext.Default, new KeylessCurseForge(), CancellationToken.None);

            await Assert.That(plan.Files.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "hidden.jar" });
        }

        [Test]
        public async Task CurseForgePlan_ModListLabels_AreUsedOnlyWhenTheCountsLineUp()
        {
            using var archive = ArchiveOf(
                ("manifest.json", """
                 {"manifestType":"minecraftModpack","manifestVersion":1,
                  "files":[{"projectID":1,"fileID":10},{"projectID":2,"fileID":20}]}
                 """),
                ("modlist.html", "<ul><li><a href=\"x\">Just Enough Items (by mezz)</a></li><li><a href=\"y\">REI</a></li></ul>"));

            var plan = await CurseForgePlanner.PlanAsync(archive, string.Empty, PackPlanContext.Default, new KeylessCurseForge(), CancellationToken.None);

            await Assert.That(plan.Pending[0].DisplayName).IsEqualTo("Just Enough Items (by mezz)");
            await Assert.That(plan.Pending[1].DisplayName).IsEqualTo("REI");
        }

        [Test]
        public async Task CurseForgePlan_ModListWithAMismatchedCount_YieldsNoLabels()
        {
            using var archive = ArchiveOf(
                ("manifest.json", """
                 {"manifestType":"minecraftModpack","manifestVersion":1,
                  "files":[{"projectID":1,"fileID":10},{"projectID":2,"fileID":20}]}
                 """),
                ("modlist.html", "<ul><li><a href=\"x\">Only One</a></li></ul>"));

            var plan = await CurseForgePlanner.PlanAsync(archive, string.Empty, PackPlanContext.Default, new KeylessCurseForge(), CancellationToken.None);

            await Assert.That(plan.Pending.All(p => p.DisplayName is null)).IsTrue();
        }

        [Test]
        public async Task CurseForgePlan_ManifestThatIsNotAModpack_IsRejected()
        {
            using var archive = ArchiveOf(("manifest.json", """{"manifestType":"somethingElse","manifestVersion":1}"""));

            await Assert.That(async () => await CurseForgePlanner.PlanAsync(archive, string.Empty, PackPlanContext.Default, new KeylessCurseForge(), CancellationToken.None))
                .Throws<PackImportException>();
        }

        [Test]
        public async Task PrismPlan_TakesTheJarsFromMinecraftMods()
        {
            using var archive = ArchiveOf(
                ("instance.cfg", "[General]\nname=1.20.1\n"),
                ("minecraft/mods/jei.jar", "PK jei"),
                ("minecraft/config/jei.toml", "x = 1"),
                ("minecraft/saves/world/level.dat", "junk"));

            var plan = PrismPlanner.Plan(archive, string.Empty, PackPlanContext.Default);

            await Assert.That(plan.Files.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "jei.jar" });
        }

        [Test]
        public async Task PrismPlan_DottedMinecraftFolder_IsAccepted()
        {
            using var archive = ArchiveOf(
                ("instance.cfg", "[General]\nname=legacy\n"),
                (".minecraft/mods/jei.jar", "PK jei"));

            var plan = PrismPlanner.Plan(archive, string.Empty, PackPlanContext.Default);

            await Assert.That(plan.Files.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "jei.jar" });
        }

        [Test]
        public async Task Plan_MrpackIndexLargerThanTheMetadataLimit_IsRejected()
        {
            using var archive = ArchiveOf(("modrinth.index.json", Padded("""{"formatVersion":1,"game":"minecraft","files":[]}""")));

            var exception = await Assert.That(() => ModrinthPlanner.Plan(archive, string.Empty, Tiny))
                .Throws<PackImportException>();

            await Assert.That(exception!.Message).Contains("MaxPackMetadataBytes");
        }

        [Test]
        public async Task Plan_CurseForgeManifestLargerThanTheMetadataLimit_IsRejected()
        {
            using var archive = ArchiveOf(("manifest.json", Padded("""{"manifestType":"minecraftModpack","manifestVersion":1,"files":[]}""")));

            var exception = await Assert.That(async () =>
                    await CurseForgePlanner.PlanAsync(archive, string.Empty, Tiny, new KeylessCurseForge(), CancellationToken.None))
                .Throws<PackImportException>();

            await Assert.That(exception!.Message).Contains("MaxPackMetadataBytes");
        }

        [Test]
        public async Task Plan_CurseForgeModlistLargerThanTheMetadataLimit_PlansWithoutLabels()
        {
            using var archive = ArchiveOf(
                ("manifest.json", """
                 {"manifestType":"minecraftModpack","manifestVersion":1,
                  "files":[{"projectID":1,"fileID":10}]}
                 """),
                ("modlist.html", Padded("<ul><li><a href=\"x\">Just Enough Items</a></li></ul>")));

            var plan = await CurseForgePlanner.PlanAsync(
                archive,
                string.Empty,
                new PackPlanContext { MaxMetadataBytes = 4096 },
                new KeylessCurseForge(),
                CancellationToken.None);

            await Assert.That(plan.Pending.Single().DisplayName).IsNull();
        }

        [Test]
        public async Task ModrinthPlan_OverrideJarReplacingAFilesEntry_DropsTheCdnCopy()
        {
            using var archive = ArchiveOf(
                ("modrinth.index.json", """
                 {"formatVersion":1,"game":"minecraft","files":[
                   {"path":"mods/patched.jar","hashes":{"sha1":"a"},"downloads":["https://cdn.modrinth.com/patched.jar"],"fileSize":1}
                 ]}
                 """),
                ("overrides/mods/patched.jar", "PK patched"));

            var plan = ModrinthPlanner.Plan(archive, string.Empty, PackPlanContext.Default);
            var file = plan.Files.Single();

            await Assert.That(file.FileName).IsEqualTo("patched.jar");
            await Assert.That(file.ZipEntry).IsEqualTo("overrides/mods/patched.jar");
            await Assert.That(file.Downloads).IsEmpty();
        }

        [Test]
        public async Task ModrinthPlan_OverrideJarReplacingAFilesEntry_DoesNotCountItAsSkipped()
        {
            using var archive = ArchiveOf(
                ("modrinth.index.json", """
                 {"formatVersion":1,"game":"minecraft","files":[
                   {"path":"mods/patched.jar","hashes":{"sha1":"a"},"downloads":["https://cdn.modrinth.com/patched.jar"],"fileSize":1},
                   {"path":"resourcepacks/pretty.zip","hashes":{"sha1":"b"},"downloads":["https://cdn.modrinth.com/pretty.zip"],"fileSize":1}
                 ]}
                 """),
                ("overrides/mods/patched.jar", "PK patched"));

            var plan = ModrinthPlanner.Plan(archive, string.Empty, PackPlanContext.Default);

            await Assert.That(plan.Skipped).IsEqualTo(1);
        }

        [Test]
        public async Task ModrinthPlan_ClientOverrideAndOverrideOfTheSameJar_KeepsTheClientOverride()
        {
            using var archive = ArchiveOf(
                ("modrinth.index.json", """{"formatVersion":1,"game":"minecraft","files":[]}"""),
                ("overrides/mods/both.jar", "PK server side"),
                ("client-overrides/mods/both.jar", "PK client side"));

            var plan = ModrinthPlanner.Plan(archive, string.Empty, PackPlanContext.Default);
            var file = plan.Files.Single();

            await Assert.That(file.ZipEntry).IsEqualTo("client-overrides/mods/both.jar");
        }

        [Test]
        public async Task ModrinthPlan_OverrideJarWithNoMatchingFilesEntry_IsPlannedAsBefore()
        {
            using var archive = ArchiveOf(
                ("modrinth.index.json", """
                 {"formatVersion":1,"game":"minecraft","files":[
                   {"path":"mods/from-cdn.jar","hashes":{"sha1":"a"},"downloads":["https://cdn.modrinth.com/from-cdn.jar"],"fileSize":1}
                 ]}
                 """),
                ("overrides/mods/custom-thing.jar", "PK custom"));

            var plan = ModrinthPlanner.Plan(archive, string.Empty, PackPlanContext.Default);

            await Assert.That(plan.Files.Select(f => f.FileName).Order().ToList())
                .IsEquivalentTo(new[] { "custom-thing.jar", "from-cdn.jar" });
        }

        [Test]
        public async Task CurseForgePlan_WithAConfiguredKey_PlansTheForgeCdnDownload()
        {
            using var archive = ArchiveOf(("manifest.json", """
                {"manifestType":"minecraftModpack","manifestVersion":1,
                 "files":[{"projectID":238222,"fileID":5678,"required":true}]}
                """));

            var resolved = new CurseForgeFile(
                238222, 5678, "jei.jar", new Uri("https://edge.forgecdn.net/files/5/678/jei.jar"), 4242, "abc", "Just Enough Items");

            var plan = await CurseForgePlanner.PlanAsync(
                archive, string.Empty, PackPlanContext.Default, new ConfiguredCurseForge(resolved), CancellationToken.None);

            var file = plan.Files.Single();

            await Assert.That(file.FileName).IsEqualTo("jei.jar");
            await Assert.That(file.Downloads.Single().Host).IsEqualTo("edge.forgecdn.net");
            await Assert.That(PackDownloadHosts.Allowed(TestLimits.Config).Contains(file.Downloads.Single().Host)).IsTrue();
        }

        [Test]
        public async Task CurseForgePlan_WithAConfiguredKey_DoesNotEmitADownloadFailedPending()
        {
            using var archive = ArchiveOf(("manifest.json", """
                {"manifestType":"minecraftModpack","manifestVersion":1,
                 "files":[{"projectID":238222,"fileID":5678,"required":true}]}
                """));

            var resolved = new CurseForgeFile(
                238222, 5678, "jei.jar", new Uri("https://mediafilez.forgecdn.net/files/5/678/jei.jar"), 4242, "abc", null);

            var plan = await CurseForgePlanner.PlanAsync(
                archive, string.Empty, PackPlanContext.Default, new ConfiguredCurseForge(resolved), CancellationToken.None);

            await Assert.That(plan.Pending).IsEmpty();
        }

        [Test]
        public async Task PrismPlan_InstanceForAnotherLoader_IsRejected()
        {
            using var archive = PrismInstance("net.minecraftforge", "47.4.20", "1.20.1");

            var exception = await Assert.That(() => PrismPlanner.Plan(archive, string.Empty, For(ModLoader.Fabric, "1.21")))
                .Throws<PackImportException>();

            await Assert.That(exception!.Message).Contains("Forge");
            await Assert.That(exception.Message).Contains("Fabric");
        }

        [Test]
        public async Task PrismPlan_InstanceForADifferentMinecraftVersion_PlansWithAWarning()
        {
            using var archive = PrismInstance("net.minecraftforge", "47.4.20", "1.20.1");

            var plan = PrismPlanner.Plan(archive, string.Empty, For(ModLoader.Forge, "1.20.4"));

            await Assert.That(plan.Files.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "jei.jar" });
            await Assert.That(plan.Warnings.Single()).Contains("1.20.1");
            await Assert.That(plan.Warnings.Single()).Contains("1.20.4");
        }

        [Test]
        public async Task PrismPlan_InstanceForTheSamePlatform_PlansWithoutAWarning()
        {
            using var archive = PrismInstance("net.fabricmc.fabric-loader", "0.15.11", "1.21");

            var plan = PrismPlanner.Plan(archive, string.Empty, For(ModLoader.Fabric, "1.21"));

            await Assert.That(plan.Warnings).IsEmpty();
        }

        [Test]
        public async Task PrismPlan_InstanceWithNoMmcPack_PlansUnchanged()
        {
            using var archive = ArchiveOf(
                ("instance.cfg", "[General]\nname=1.20.1\n"),
                ("minecraft/mods/jei.jar", "PK jei"));

            var plan = PrismPlanner.Plan(archive, string.Empty, For(ModLoader.Fabric, "1.21"));

            await Assert.That(plan.Files.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "jei.jar" });
            await Assert.That(plan.Warnings).IsEmpty();
        }

        [Test]
        public async Task PrismPlan_ServerWithNoLoaderConfigured_SkipsThePlatformCheck()
        {
            using var archive = PrismInstance("net.minecraftforge", "47.4.20", "1.20.1");

            var plan = PrismPlanner.Plan(archive, string.Empty, PackPlanContext.Default);

            await Assert.That(plan.Files.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "jei.jar" });
            await Assert.That(plan.Warnings).IsEmpty();
        }

        [Test]
        public async Task CurseForgePlan_ManifestForAnotherLoader_IsRejected()
        {
            using var archive = ArchiveOf(("manifest.json", """
                {"manifestType":"minecraftModpack","manifestVersion":1,
                 "minecraft":{"version":"1.20.1","modLoaders":[{"id":"forge-47.4.0","primary":true}]},
                 "files":[]}
                """));

            var exception = await Assert.That(async () => await CurseForgePlanner.PlanAsync(
                    archive, string.Empty, For(ModLoader.NeoForge, "1.20.1"), new KeylessCurseForge(), CancellationToken.None))
                .Throws<PackImportException>();

            await Assert.That(exception!.Message).Contains("NeoForge");
        }

        [Test]
        public async Task CurseForgePlan_ManifestForTheSamePlatform_PlansCleanly()
        {
            using var archive = ArchiveOf(
                ("manifest.json", """
                 {"manifestType":"minecraftModpack","manifestVersion":1,
                  "minecraft":{"version":"1.20.1","modLoaders":[{"id":"forge-47.4.0","primary":true}]},
                  "files":[],"overrides":"overrides"}
                 """),
                ("overrides/mods/cc-tweaked.jar", "PK cc"));

            var plan = await CurseForgePlanner.PlanAsync(
                archive, string.Empty, For(ModLoader.Forge, "1.20.1"), new KeylessCurseForge(), CancellationToken.None);

            await Assert.That(plan.Files.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "cc-tweaked.jar" });
            await Assert.That(plan.Warnings).IsEmpty();
        }

        [Test]
        public async Task ModrinthPlan_DependenciesForAnotherLoader_IsRejected()
        {
            using var archive = ArchiveOf(("modrinth.index.json", """
                {"formatVersion":1,"game":"minecraft",
                 "dependencies":{"minecraft":"1.20.1","forge":"47.4.0"},
                 "files":[]}
                """));

            var exception = await Assert.That(() =>
                    ModrinthPlanner.Plan(archive, string.Empty, For(ModLoader.Fabric, "1.20.1")))
                .Throws<PackImportException>();

            await Assert.That(exception!.Message).Contains("Forge");
        }

        [Test]
        public async Task ModrinthPlan_DependenciesForADifferentMinecraftVersion_PlansWithAWarning()
        {
            using var archive = ArchiveOf(("modrinth.index.json", """
                {"formatVersion":1,"game":"minecraft",
                 "dependencies":{"minecraft":"1.20.1","fabric-loader":"0.15.11"},
                 "files":[]}
                """));

            var plan = ModrinthPlanner.Plan(archive, string.Empty, For(ModLoader.Fabric, "1.21"));

            await Assert.That(plan.Warnings.Single()).Contains("1.20.1");
        }

        [Test]
        public async Task ModrinthPlan_WithNoDependenciesBlock_SkipsThePlatformCheck()
        {
            using var archive = ArchiveOf(("modrinth.index.json", """{"formatVersion":1,"game":"minecraft","files":[]}"""));

            var plan = ModrinthPlanner.Plan(archive, string.Empty, For(ModLoader.Fabric, "1.21"));

            await Assert.That(plan.Warnings).IsEmpty();
        }

        private static readonly PackPlanContext Tiny = new() { MaxMetadataBytes = 64 };

        private static string Padded(string json) => json + new string(' ', 8192);

        private static ZipArchive PrismInstance(string loaderUid, string loaderVersion, string minecraftVersion) =>
            ArchiveOf(
                ("instance.cfg", $"[General]\nname={minecraftVersion}\n"),
                ("mmc-pack.json", $$"""
                 {"formatVersion":1,"components":[
                   {"uid":"{{LoaderIds.MinecraftUid}}","version":"{{minecraftVersion}}","important":true},
                   {"uid":"{{loaderUid}}","version":"{{loaderVersion}}"}
                 ]}
                 """),
                ("minecraft/mods/jei.jar", "PK jei"));

        [Test]
        public async Task JarArchivePlan_TakesEveryJarByBasename()
        {
            using var archive = ArchiveOf(
                ("jei.jar", "PK jei"),
                ("nested/folder/rei.jar", "PK rei"),
                ("readme.txt", "hi"),
                ("__MACOSX/._jei.jar", "junk"));

            var plan = JarArchivePlanner.Plan(archive);

            await Assert.That(plan.Files.Select(f => f.FileName).Order().ToList())
                .IsEquivalentTo(new[] { "jei.jar", "rei.jar" });
        }
    }
}
