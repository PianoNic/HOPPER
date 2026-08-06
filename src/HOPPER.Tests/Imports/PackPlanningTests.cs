using System.IO.Compression;
using System.Text;
using HOPPER.Application.Imports;
using HOPPER.Domain.Enums;

namespace HOPPER.Tests.Imports
{
    /// <summary>
    /// Detection and planning, against fixtures built in code rather than against the 51 MB real packs
    /// they were derived from. Two rules here are the ones that bite if missed, and both are asserted
    /// directly: overrides/mods is always ingested (it is where the non-redistributable jars hide), and
    /// a CurseForge pack with no API key yields pending entries rather than silence.
    /// </summary>
    public class PackPlanningTests
    {
        /// <summary>Stands in for a real API client. IsConfigured=false is the shipped default: HOPPER
        /// neither hardcodes nor bundles a CurseForge key.</summary>
        private sealed class KeylessCurseForge : ICurseForgeClient
        {
            public bool IsConfigured => false;

            public Task<IReadOnlyDictionary<int, CurseForgeFile>> ResolveAsync(IReadOnlyList<int> fileIds, CancellationToken cancellationToken) =>
                Task.FromResult<IReadOnlyDictionary<int, CurseForgeFile>>(new Dictionary<int, CurseForgeFile>());

            public Task<Uri?> FindOnModrinthBySha1Async(string sha1, CancellationToken cancellationToken) =>
                Task.FromResult<Uri?>(null);
        }

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

        // ---- detection ---------------------------------------------------------------------

        [Test]
        public async Task Detect_MrpackWithADecoyManifestInsideOverrides_IsStillModrinth()
        {
            // Rules 1-3 match the FULL path precisely because manifest.json is an ordinary filename
            // that turns up inside overrides/ in real packs. A basename match here would read a
            // CurseForge pack out of a Modrinth one and then find no files[] at all.
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
            // Rule 4 matches the BASENAME anywhere, because whoever shared the export may have zipped
            // the folder rather than its contents.
            using var archive = ArchiveOf(
                ("MyPack/instance.cfg", "[General]\nname=MyPack\n"),
                ("MyPack/mmc-pack.json", "{}"),
                ("MyPack/minecraft/mods/jei.jar", "PK jei"));

            var detection = PackDetector.Detect(archive);

            await Assert.That(detection.Format).IsEqualTo(PackFormat.PrismInstance);
            await Assert.That(detection.Prefix).IsEqualTo("MyPack/");
        }

        [Test]
        public async Task Detect_PrismZipWrappingAnMrpack_DelegatesToModrinth()
        {
            // A pack someone downloaded and re-zipped without ever installing it. Detection re-runs
            // the first three rules against the stripped tree so it resolves to its real format.
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
            // Recognised on purpose: "not a recognised modpack" would read as a corrupt download.
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

        // ---- Modrinth ----------------------------------------------------------------------

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

            var plan = ModrinthPlanner.Plan(archive, string.Empty);
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
            // The rule that bites if missed. Better MC ships 21 jars here and no URLs for them: a pack
            // imported without overrides/mods is a pack that does not launch.
            using var archive = ArchiveOf(
                ("modrinth.index.json", """{"formatVersion":1,"game":"minecraft","files":[]}"""),
                ("overrides/mods/custom-thing.jar", "PK custom"),
                ("overrides/config/some.toml", "x = 1"),
                ("client-overrides/mods/client-only.jar", "PK client"),
                ("server-overrides/mods/server-only.jar", "PK server"));

            var plan = ModrinthPlanner.Plan(archive, string.Empty);

            // server-overrides is deliberately excluded: those files exist because they are wrong on a
            // client, which is the only kind of machine HOPPER sends jars to.
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

            var plan = ModrinthPlanner.Plan(archive, string.Empty);

            await Assert.That(plan.Files.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "keep.jar" });
            await Assert.That(plan.Skipped).IsEqualTo(2);
        }

        [Test]
        public async Task ModrinthPlan_ClientUnsupportedEntry_IsSkipped()
        {
            // env is optional per spec, so absent means "install everywhere". Only an explicit
            // client:"unsupported" is a reason to leave a jar out - HOPPER feeds game clients.
            using var archive = ArchiveOf(("modrinth.index.json", """
                {"formatVersion":1,"game":"minecraft","files":[
                  {"path":"mods/server-side.jar","hashes":{"sha1":"a"},"env":{"client":"unsupported","server":"required"},
                   "downloads":["https://cdn.modrinth.com/a.jar"],"fileSize":1},
                  {"path":"mods/no-env.jar","hashes":{"sha1":"b"},"downloads":["https://cdn.modrinth.com/b.jar"],"fileSize":1}
                ]}
                """));

            var plan = ModrinthPlanner.Plan(archive, string.Empty);

            await Assert.That(plan.Files.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "no-env.jar" });
        }

        [Test]
        public async Task ModrinthPlan_WrongFormatVersion_IsRejected()
        {
            using var archive = ArchiveOf(("modrinth.index.json", """{"formatVersion":2,"game":"minecraft","files":[]}"""));

            await Assert.That(() => ModrinthPlanner.Plan(archive, string.Empty)).Throws<PackImportException>();
        }

        // ---- CurseForge --------------------------------------------------------------------

        [Test]
        public async Task CurseForgePlan_WithoutAnApiKey_TurnsEveryManifestEntryIntoAPending()
        {
            // A files[] entry is two integers: no filename, no URL, no hash, no size. Offline there is
            // nothing to resolve, so the honest outcome is a pending row per entry - which is exactly
            // what Prism's BlockedModsDialog exists to work through.
            using var archive = ArchiveOf(
                ("manifest.json", """
                 {"manifestType":"minecraftModpack","manifestVersion":1,"name":"ATM9","version":"1.1.1",
                  "minecraft":{"version":"1.20.1","modLoaders":[{"id":"forge-47.4.0","primary":true}]},
                  "files":[{"projectID":351491,"fileID":6366217,"required":true},
                           {"projectID":514045,"fileID":4938351,"required":true}],
                  "overrides":"overrides"}
                 """),
                ("overrides/mods/cc-tweaked.jar", "PK cc"));

            var plan = await CurseForgePlanner.PlanAsync(archive, string.Empty, new KeylessCurseForge(), CancellationToken.None);

            await Assert.That(plan.Files.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "cc-tweaked.jar" });
            await Assert.That(plan.Pending).Count().IsEqualTo(2);
            await Assert.That(plan.Pending.All(p => p.Reason == PendingReason.NoApiKey)).IsTrue();
            await Assert.That(plan.Pending[0].ProjectId).IsEqualTo(351491);
            await Assert.That(plan.Pending[0].FileId).IsEqualTo(6366217);
            // No hash is knowable, so a supplied jar can only ever be asserted, never verified.
            await Assert.That(plan.Pending[0].ExpectedSha1).IsNull();
        }

        [Test]
        public async Task CurseForgePlan_OverridesFolderName_IsReadNotAssumed()
        {
            // "overrides" is a field, not a constant. Hardcoding it silently loses every jar in a pack
            // that named the folder anything else.
            using var archive = ArchiveOf(
                ("manifest.json", """
                 {"manifestType":"minecraftModpack","manifestVersion":1,"files":[],"overrides":"custom"}
                 """),
                ("custom/mods/hidden.jar", "PK hidden"));

            var plan = await CurseForgePlanner.PlanAsync(archive, string.Empty, new KeylessCurseForge(), CancellationToken.None);

            await Assert.That(plan.Files.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "hidden.jar" });
        }

        [Test]
        public async Task CurseForgePlan_ModListLabels_AreUsedOnlyWhenTheCountsLineUp()
        {
            // modlist.html carries no ids, so it can be lined up positionally at best. Two anchors for
            // two files[] entries is usable; anything else has to be discarded rather than guessed at.
            using var archive = ArchiveOf(
                ("manifest.json", """
                 {"manifestType":"minecraftModpack","manifestVersion":1,
                  "files":[{"projectID":1,"fileID":10},{"projectID":2,"fileID":20}]}
                 """),
                ("modlist.html", "<ul><li><a href=\"x\">Just Enough Items (by mezz)</a></li><li><a href=\"y\">REI</a></li></ul>"));

            var plan = await CurseForgePlanner.PlanAsync(archive, string.Empty, new KeylessCurseForge(), CancellationToken.None);

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

            var plan = await CurseForgePlanner.PlanAsync(archive, string.Empty, new KeylessCurseForge(), CancellationToken.None);

            await Assert.That(plan.Pending.All(p => p.DisplayName is null)).IsTrue();
        }

        [Test]
        public async Task CurseForgePlan_ManifestThatIsNotAModpack_IsRejected()
        {
            using var archive = ArchiveOf(("manifest.json", """{"manifestType":"somethingElse","manifestVersion":1}"""));

            await Assert.That(async () => await CurseForgePlanner.PlanAsync(archive, string.Empty, new KeylessCurseForge(), CancellationToken.None))
                .Throws<PackImportException>();
        }

        // ---- Prism / plain zip -------------------------------------------------------------

        [Test]
        public async Task PrismPlan_TakesTheJarsFromMinecraftMods()
        {
            using var archive = ArchiveOf(
                ("instance.cfg", "[General]\nname=1.20.1\n"),
                ("minecraft/mods/jei.jar", "PK jei"),
                ("minecraft/config/jei.toml", "x = 1"),
                ("minecraft/saves/world/level.dat", "junk"));

            var plan = PrismPlanner.Plan(archive, string.Empty);

            await Assert.That(plan.Files.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "jei.jar" });
        }

        [Test]
        public async Task PrismPlan_DottedMinecraftFolder_IsAccepted()
        {
            // Older MultiMC instances use ".minecraft"; Prism's own gameRoot() prefers "minecraft"
            // when both exist and falls back to the dotted form otherwise.
            using var archive = ArchiveOf(
                ("instance.cfg", "[General]\nname=legacy\n"),
                (".minecraft/mods/jei.jar", "PK jei"));

            var plan = PrismPlanner.Plan(archive, string.Empty);

            await Assert.That(plan.Files.Select(f => f.FileName).ToList()).IsEquivalentTo(new[] { "jei.jar" });
        }

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
