using HOPPER.Application.Modrinth;

namespace HOPPER.Tests.Modrinth
{
    public class DependencyResolverTests
    {
        private const string Loader = "forge";
        private const string GameVersion = "1.20.1";

        private static ResolveRequest Request(IEnumerable<string> roots, params InstalledMod[] installed) => new()
        {
            RootVersionIds = roots.ToList(),
            Loader = Loader,
            GameVersion = GameVersion,
            Installed = installed,
        };

        private static Task<ResolveResult> ResolveAsync(FakeModrinthClient client, ResolveRequest request) =>
            new ModrinthDependencyResolver(client).ResolveAsync(request, CancellationToken.None);

        [Test]
        public async Task Resolve_RequiredTwoLevelsDeep_PullsInBothAndRecordsWhoAskedForThem()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PBACKPACK", "v-backpack", "Sophisticated Backpacks", "backpacks.jar",
                dependencies: FakeModrinthClient.Required("PCORE"));
            client.AddMod("PCORE", "v-core", "Sophisticated Core", "core.jar",
                dependencies: FakeModrinthClient.Required("PLIB"));
            client.AddMod("PLIB", "v-lib", "Some Library", "lib.jar");

            var result = await ResolveAsync(client, Request(["v-backpack"]));

            await Assert.That(result.Nodes.Count).IsEqualTo(3);

            var core = result.Nodes.Single(n => n.ProjectId == "PCORE");
            await Assert.That(core.Kind).IsEqualTo(PlanNodeKind.Required);
            await Assert.That(core.Depth).IsEqualTo(1);
            await Assert.That(core.RequiredBy).Contains("Sophisticated Backpacks");

            var lib = result.Nodes.Single(n => n.ProjectId == "PLIB");
            await Assert.That(lib.Depth).IsEqualTo(2);
            await Assert.That(lib.RequiredBy).Contains("Sophisticated Core");

            await Assert.That(result.Nodes.Single(n => n.ProjectId == "PBACKPACK").RequiredBy).IsEmpty();
            await Assert.That(result.Blocked).IsFalse();
        }

        [Test]
        public async Task Resolve_PinnedDependency_UsesThatExactVersionAndNeverAsksForAList()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PCREATE", "v-create", "Create", "create.jar",
                dependencies: FakeModrinthClient.RequiredVersion("v-flywheel-old"));
            client.AddMod("PFLYWHEEL", "v-flywheel-old", "Flywheel", "flywheel-old.jar");

            client.AddMod("PFLYWHEEL", "v-flywheel-new", "Flywheel", "flywheel-new.jar");

            var result = await ResolveAsync(client, Request(["v-create"]));

            var flywheel = result.Nodes.Single(n => n.ProjectId == "PFLYWHEEL");
            await Assert.That(flywheel.VersionId).IsEqualTo("v-flywheel-old");
            await Assert.That(flywheel.Pinned).IsTrue();
            await Assert.That(client.Calls.Any(c => c.StartsWith("list:PFLYWHEEL"))).IsFalse();
        }

        [Test]
        public async Task Resolve_UnpinnedDependency_TakesTheNewestRelease()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PROOT", "v-root", "Root", "root.jar",
                dependencies: FakeModrinthClient.Required("PDEP"));

            client.AddMod("PDEP", "v-dep-old", "Dep", "dep-old.jar");
            client.AddMod("PDEP", "v-dep-beta", "Dep", "dep-beta.jar", versionType: "beta");
            client.AddMod("PDEP", "v-dep-new", "Dep", "dep-new.jar");

            var result = await ResolveAsync(client, Request(["v-root"]));

            var dep = result.Nodes.Single(n => n.ProjectId == "PDEP");
            await Assert.That(dep.VersionId).IsEqualTo("v-dep-new");
            await Assert.That(dep.Prerelease).IsFalse();
        }

        [Test]
        public async Task Resolve_DependencyWithNoRelease_TakesTheBetaAndFlagsIt()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PROOT", "v-root", "Root", "root.jar",
                dependencies: FakeModrinthClient.Required("PDEP"));
            client.AddMod("PDEP", "v-dep-alpha", "Dep", "dep-alpha.jar", versionType: "alpha");
            client.AddMod("PDEP", "v-dep-beta", "Dep", "dep-beta.jar", versionType: "beta");

            var result = await ResolveAsync(client, Request(["v-root"]));

            var dep = result.Nodes.Single(n => n.ProjectId == "PDEP");
            await Assert.That(dep.VersionId).IsEqualTo("v-dep-beta");
            await Assert.That(dep.Prerelease).IsTrue();
        }

        [Test]
        public async Task Resolve_DependencyWithNoMatchingVersionAtAll_IsSurfacedNotFatal()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PROOT", "v-root", "Root", "root.jar",
                dependencies: FakeModrinthClient.Required("PGONE"));

            client.Projects["PGONE"] = new ModrinthProject { Id = "PGONE", Title = "Abandoned Mod" };

            var result = await ResolveAsync(client, Request(["v-root"]));

            await Assert.That(result.Nodes.Count).IsEqualTo(1);
            await Assert.That(result.Unresolvable.Any(u => u.Name == "Abandoned Mod")).IsTrue();
        }

        [Test]
        public async Task Resolve_CycleBetweenTwoMods_Terminates()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PA", "v-a", "Mod A", "a.jar", dependencies: FakeModrinthClient.Required("PB"));
            client.AddMod("PB", "v-b", "Mod B", "b.jar", dependencies: FakeModrinthClient.Required("PA"));

            var result = await ResolveAsync(client, Request(["v-a"]));

            await Assert.That(result.Nodes.Count).IsEqualTo(2);
            await Assert.That(result.Nodes.Select(n => n.ProjectId).Order().ToList()).IsEquivalentTo(new[] { "PA", "PB" });
        }

        [Test]
        public async Task Resolve_LongerCycle_Terminates()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PA", "v-a", "A", "a.jar", dependencies: FakeModrinthClient.Required("PB"));
            client.AddMod("PB", "v-b", "B", "b.jar", dependencies: FakeModrinthClient.Required("PC"));
            client.AddMod("PC", "v-c", "C", "c.jar", dependencies: FakeModrinthClient.Required("PA"));

            var result = await ResolveAsync(client, Request(["v-a"]));

            await Assert.That(result.Nodes.Count).IsEqualTo(3);
        }

        [Test]
        public async Task Resolve_SameProjectAtTwoVersions_KeepsOneAndSaysSo()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PA", "v-a", "A", "a.jar", dependencies: FakeModrinthClient.RequiredVersion("v-lib-1"));
            client.AddMod("PB", "v-b", "B", "b.jar", dependencies: FakeModrinthClient.RequiredVersion("v-lib-2"));
            client.AddMod("PLIB", "v-lib-1", "Lib", "lib-1.jar");
            client.AddMod("PLIB", "v-lib-2", "Lib", "lib-2.jar");

            var result = await ResolveAsync(client, Request(["v-a", "v-b"]));

            await Assert.That(result.Nodes.Count(n => n.ProjectId == "PLIB")).IsEqualTo(1);
            await Assert.That(result.Warnings.Any(w => w.Contains("two versions"))).IsTrue();
        }

        [Test]
        public async Task Resolve_ModReachedByTwoPaths_IsOneNodeWithBothParents()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PA", "v-a", "A", "a.jar", dependencies: FakeModrinthClient.Required("PLIB"));
            client.AddMod("PB", "v-b", "B", "b.jar", dependencies: FakeModrinthClient.Required("PLIB"));
            client.AddMod("PLIB", "v-lib", "Lib", "lib.jar");

            var result = await ResolveAsync(client, Request(["v-a", "v-b"]));

            var lib = result.Nodes.Single(n => n.ProjectId == "PLIB");
            await Assert.That(lib.RequiredBy.Order().ToList()).IsEquivalentTo(new[] { "A", "B" });
        }

        [Test]
        public async Task Resolve_Embedded_IsListedButNeverAdded()
        {
            var client = new FakeModrinthClient();
            client.AddMod("POWO", "v-owo", "owo-lib", "owo.jar",
                dependencies: FakeModrinthClient.Embedded("PBUNDLED"));
            client.AddMod("PBUNDLED", "v-bundled", "Bundled Thing", "bundled.jar");

            var result = await ResolveAsync(client, Request(["v-owo"]));

            await Assert.That(result.Nodes.Count).IsEqualTo(1);
            await Assert.That(result.Nodes.Any(n => n.ProjectId == "PBUNDLED")).IsFalse();
            await Assert.That(result.Embedded.Single().BundledBy).IsEqualTo("owo-lib");
        }

        [Test]
        public async Task Resolve_Optional_IsOfferedButItsOwnDependenciesAreNotWalked()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PBACKPACK", "v-backpack", "Backpacks", "backpacks.jar",
                dependencies: FakeModrinthClient.Optional("PJEI"));
            client.AddMod("PJEI", "v-jei", "JEI", "jei.jar", dependencies: FakeModrinthClient.Required("PJEIDEP"));
            client.AddMod("PJEIDEP", "v-jeidep", "JEI Dep", "jeidep.jar");

            var result = await ResolveAsync(client, Request(["v-backpack"]));

            await Assert.That(result.Nodes.Count).IsEqualTo(1);
            await Assert.That(result.Optional.Single().ProjectId).IsEqualTo("PJEI");
            await Assert.That(result.Optional.Single().Kind).IsEqualTo(PlanNodeKind.Optional);

            await Assert.That(result.Nodes.Concat(result.Optional).Any(n => n.ProjectId == "PJEIDEP")).IsFalse();
        }

        [Test]
        public async Task Resolve_TickedOptional_BecomesARootAndDragsItsOwnRequirementsIntoView()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PBACKPACK", "v-backpack", "Backpacks", "backpacks.jar",
                dependencies: FakeModrinthClient.Optional("PJEI"));
            client.AddMod("PJEI", "v-jei", "JEI", "jei.jar", dependencies: FakeModrinthClient.Required("PJEIDEP"));
            client.AddMod("PJEIDEP", "v-jeidep", "JEI Dep", "jeidep.jar");

            var result = await ResolveAsync(client, Request(["v-backpack", "v-jei"]));

            await Assert.That(result.Nodes.Select(n => n.ProjectId).Order().ToList())
                .IsEquivalentTo(new[] { "PBACKPACK", "PJEI", "PJEIDEP" });
        }

        [Test]
        public async Task Resolve_OptionalThatIsAlsoRequired_IsNotOfferedTwice()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PA", "v-a", "A", "a.jar", dependencies: FakeModrinthClient.Optional("PLIB"));
            client.AddMod("PB", "v-b", "B", "b.jar", dependencies: FakeModrinthClient.Required("PLIB"));
            client.AddMod("PLIB", "v-lib", "Lib", "lib.jar");

            var result = await ResolveAsync(client, Request(["v-a", "v-b"]));

            await Assert.That(result.Nodes.Any(n => n.ProjectId == "PLIB")).IsTrue();
            await Assert.That(result.Optional).IsEmpty();
        }

        [Test]
        public async Task Resolve_IncompatibleWithSomethingOnTheServer_Blocks()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PEMBEDDIUM", "v-embeddium", "Embeddium", "embeddium.jar",
                dependencies: FakeModrinthClient.Incompatible("PRUBIDIUM"));
            client.Projects["PRUBIDIUM"] = new ModrinthProject { Id = "PRUBIDIUM", Title = "Rubidium" };

            var result = await ResolveAsync(client, Request(
                ["v-embeddium"],
                new InstalledMod("PRUBIDIUM", "v-rubidium", "rubidium.jar")));

            await Assert.That(result.Blocked).IsTrue();
            var note = result.Incompatible.Single();
            await Assert.That(note.Applies).IsTrue();
            await Assert.That(note.DeclaredBy).IsEqualTo("Embeddium");
            await Assert.That(note.Title).IsEqualTo("Rubidium");
        }

        [Test]
        public async Task Resolve_IncompatibleWithSomethingAbsent_WarnsWithoutBlocking()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PEMBEDDIUM", "v-embeddium", "Embeddium", "embeddium.jar",
                dependencies: FakeModrinthClient.Incompatible("PRUBIDIUM"));

            var result = await ResolveAsync(client, Request(["v-embeddium"]));

            await Assert.That(result.Blocked).IsFalse();
            await Assert.That(result.Incompatible.Single().Applies).IsFalse();
        }

        [Test]
        public async Task Resolve_TwoIncompatibleModsInOneSelection_Blocks()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PA", "v-a", "A", "a.jar", dependencies: FakeModrinthClient.Incompatible("PB"));
            client.AddMod("PB", "v-b", "B", "b.jar");

            var result = await ResolveAsync(client, Request(["v-a", "v-b"]));

            await Assert.That(result.Blocked).IsTrue();
        }

        [Test]
        public async Task Resolve_DependencyWithNoIdsAtAll_IsShownAndDoesNotFailThePlan()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PA", "v-a", "A", "a.jar",
                dependencies: FakeModrinthClient.Unnameable("some-external-thing.jar"));

            var result = await ResolveAsync(client, Request(["v-a"]));

            await Assert.That(result.Nodes.Count).IsEqualTo(1);
            var note = result.Unresolvable.Single();
            await Assert.That(note.Name).IsEqualTo("some-external-thing.jar");
            await Assert.That(note.RequestedBy).IsEqualTo("A");
        }

        [Test]
        public async Task Resolve_UnknownDependencyType_IsIgnoredRatherThanGuessedAt()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PA", "v-a", "A", "a.jar",
                dependencies: new ModrinthDependency { ProjectId = "PB", DependencyType = "something-new" });
            client.AddMod("PB", "v-b", "B", "b.jar");

            var result = await ResolveAsync(client, Request(["v-a"]));

            await Assert.That(result.Nodes.Count).IsEqualTo(1);
        }

        [Test]
        public async Task Resolve_ExactVersionAlreadyInstalled_IsMarkedAlreadyInstalled()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PA", "v-a", "A", "a.jar");

            var result = await ResolveAsync(client, Request(
                ["v-a"], new InstalledMod("PA", "v-a", "a.jar")));

            await Assert.That(result.Nodes.Single().Status).IsEqualTo(PlanNodeStatus.AlreadyInstalled);
        }

        [Test]
        public async Task Resolve_OtherVersionInstalled_IsMarkedForAnExplicitReplace()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PA", "v-a-new", "A", "a-new.jar");

            var result = await ResolveAsync(client, Request(
                ["v-a-new"], new InstalledMod("PA", "v-a-old", "a-old.jar")));

            await Assert.That(result.Nodes.Single().Status).IsEqualTo(PlanNodeStatus.OtherVersionInstalled);
        }

        [Test]
        public async Task Resolve_FileNameTakenByAHandUploadedJar_IsMarkedFileNameTaken()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PA", "v-a", "A", "jei.jar");

            var result = await ResolveAsync(client, Request(
                ["v-a"], new InstalledMod(null, null, "jei.jar")));

            await Assert.That(result.Nodes.Single().Status).IsEqualTo(PlanNodeStatus.FileNameTaken);
        }

        [Test]
        public async Task Resolve_NothingInstalled_IsAllNew()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PA", "v-a", "A", "a.jar", dependencies: FakeModrinthClient.Required("PB"));
            client.AddMod("PB", "v-b", "B", "b.jar");

            var result = await ResolveAsync(client, Request(["v-a"]));

            await Assert.That(result.Nodes.All(n => n.Status == PlanNodeStatus.New)).IsTrue();
        }

        [Test]
        public async Task Resolve_HugeDependencyTree_IsRefusedRatherThanWalked()
        {
            var client = new FakeModrinthClient();
            var dependencies = new List<ModrinthDependency>();

            for (var i = 0; i < 150; i++)
            {
                client.AddMod($"P{i}", $"v-{i}", $"Mod {i}", $"mod-{i}.jar");
                dependencies.Add(FakeModrinthClient.RequiredVersion($"v-{i}"));
            }

            client.AddMod("PROOT", "v-root", "Root", "root.jar", dependencies: dependencies.ToArray());

            await Assert.That(async () => await ResolveAsync(client, Request(["v-root"])))
                .Throws<ResolveBudgetExceededException>();
        }

        [Test]
        public async Task Resolve_OneLevel_CostsOneBulkCallPerKindRatherThanOnePerMod()
        {
            var client = new FakeModrinthClient();
            var dependencies = new List<ModrinthDependency>();

            for (var i = 0; i < 5; i++)
            {
                client.AddMod($"P{i}", $"v-{i}", $"Mod {i}", $"mod-{i}.jar");
                dependencies.Add(FakeModrinthClient.RequiredVersion($"v-{i}"));
            }

            client.AddMod("PROOT", "v-root", "Root", "root.jar", dependencies: dependencies.ToArray());

            var result = await ResolveAsync(client, Request(["v-root"]));

            await Assert.That(result.Nodes.Count).IsEqualTo(6);

            await Assert.That(client.Calls.Count(c => c.StartsWith("versions:"))).IsEqualTo(2);
        }

        [Test]
        public async Task Resolve_VersionModrinthNoLongerHas_IsReportedRatherThanSilentlyDropped()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PA", "v-a", "A", "a.jar");

            var result = await ResolveAsync(client, Request(["v-a", "v-vanished"]));

            await Assert.That(result.Nodes.Count).IsEqualTo(1);
            await Assert.That(result.Warnings.Any(w => w.Contains("no longer on Modrinth"))).IsTrue();
        }

        [Test]
        public async Task Resolve_VersionWithNoDownloadableJar_IsSurfacedNotAdded()
        {
            var client = new FakeModrinthClient();
            client.Versions["v-empty"] = new ModrinthVersion
            {
                Id = "v-empty",
                ProjectId = "PEMPTY",
                Name = "Metadata Only",
                VersionType = "release",
            };

            var result = await ResolveAsync(client, Request(["v-empty"]));

            await Assert.That(result.Nodes).IsEmpty();
            await Assert.That(result.Unresolvable.Single().Name).IsEqualTo("Metadata Only");
        }

        [Test]
        public async Task Resolve_NothingSelected_IsAnEmptyPlanRatherThanAnError()
        {
            var client = new FakeModrinthClient();

            var result = await ResolveAsync(client, Request([]));

            await Assert.That(result.Nodes).IsEmpty();
            await Assert.That(result.Blocked).IsFalse();
            await Assert.That(client.Calls).IsEmpty();
        }

        [Test]
        public async Task Resolve_NodeCarriesEverythingInstallNeedsToFetchIt()
        {
            var client = new FakeModrinthClient();
            client.AddMod("PA", "v-a", "Mod A", "a.jar", size: 4242);

            var result = await ResolveAsync(client, Request(["v-a"]));

            var node = result.Nodes.Single();
            await Assert.That(node.FileName).IsEqualTo("a.jar");
            await Assert.That(node.FileSize).IsEqualTo(4242L);
            await Assert.That(node.DownloadUrl).StartsWith("https://cdn.modrinth.com/");
            await Assert.That(node.Sha1).IsNotNull();
            await Assert.That(node.Sha512).IsNotNull();
            await Assert.That(node.ProjectTitle).IsEqualTo("Mod A");
        }
    }
}
