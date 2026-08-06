using HOPPER.Application.Modrinth;

namespace HOPPER.Tests.Modrinth
{
    /// <summary>
    /// The resolver is the piece the whole "nothing arrives unseen" promise rests on: whatever it puts
    /// in Nodes is exactly what install writes, and whatever it leaves out is never fetched. So the
    /// cases that matter are the ones where a wrong answer would be invisible - an embedded library
    /// added twice, a cycle looping against someone else's API, an optional quietly pulled in, an
    /// incompatibility noticed only after the jars are on disk.
    ///
    /// Every test drives FakeModrinthClient. Nothing here touches the live API.
    /// </summary>
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

        // ---- transitive --------------------------------------------------------------------

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

            // The root itself is not "required by" anything, which is what the dialog's caption keys on.
            await Assert.That(result.Nodes.Single(n => n.ProjectId == "PBACKPACK").RequiredBy).IsEmpty();
            await Assert.That(result.Blocked).IsFalse();
        }

        [Test]
        public async Task Resolve_PinnedDependency_UsesThatExactVersionAndNeverAsksForAList()
        {
            // version_id set means the author named a build, not a project. Re-resolving it would
            // silently install a different one than the pack author pinned.
            var client = new FakeModrinthClient();
            client.AddMod("PCREATE", "v-create", "Create", "create.jar",
                dependencies: FakeModrinthClient.RequiredVersion("v-flywheel-old"));
            client.AddMod("PFLYWHEEL", "v-flywheel-old", "Flywheel", "flywheel-old.jar");

            // A newer one exists and must NOT be chosen.
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

            // Inserted oldest first, so the newest ends up at the head - as the real endpoint returns it.
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
            // A mod whose only Forge 1.20.1 build is a beta is still the build that exists. Taking it
            // is right; taking it silently is not, hence the flag the dialog renders as a badge.
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

            // The project exists but publishes nothing for this loader and Minecraft version.
            client.Projects["PGONE"] = new ModrinthProject { Id = "PGONE", Title = "Abandoned Mod" };

            var result = await ResolveAsync(client, Request(["v-root"]));

            await Assert.That(result.Nodes.Count).IsEqualTo(1);
            await Assert.That(result.Unresolvable.Any(u => u.Name == "Abandoned Mod")).IsTrue();
        }

        // ---- cycles and duplicates ---------------------------------------------------------

        [Test]
        public async Task Resolve_CycleBetweenTwoMods_Terminates()
        {
            // The visited set is keyed on PROJECT id, not version id. That is the only thing standing
            // between a mutually-referencing pair and an infinite walk against someone else's API.
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
            // Two mods pinning different builds of the same library. Installing both is impossible -
            // one filename, one row - so the first wins and the admin is told rather than left to
            // wonder why the pin was ignored.
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

        // ---- the four dependency types -----------------------------------------------------

        [Test]
        public async Task Resolve_Embedded_IsListedButNeverAdded()
        {
            // The jar is already inside the parent. Adding it ships the same classes twice and Forge
            // may reject the duplicate outright.
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
            // An unticked optional is a suggestion. Walking its graph would put mods in the "will be
            // added" list that nothing is going to add.
            var client = new FakeModrinthClient();
            client.AddMod("PBACKPACK", "v-backpack", "Backpacks", "backpacks.jar",
                dependencies: FakeModrinthClient.Optional("PJEI"));
            client.AddMod("PJEI", "v-jei", "JEI", "jei.jar", dependencies: FakeModrinthClient.Required("PJEIDEP"));
            client.AddMod("PJEIDEP", "v-jeidep", "JEI Dep", "jeidep.jar");

            var result = await ResolveAsync(client, Request(["v-backpack"]));

            await Assert.That(result.Nodes.Count).IsEqualTo(1);
            await Assert.That(result.Optional.Single().ProjectId).IsEqualTo("PJEI");
            await Assert.That(result.Optional.Single().Kind).IsEqualTo(PlanNodeKind.Optional);

            // JEI's own dependency is nowhere - not in Nodes and not in Optional.
            await Assert.That(result.Nodes.Concat(result.Optional).Any(n => n.ProjectId == "PJEIDEP")).IsFalse();
        }

        [Test]
        public async Task Resolve_TickedOptional_BecomesARootAndDragsItsOwnRequirementsIntoView()
        {
            // This is the mechanism behind the promise. Ticking an optional re-runs the resolve with
            // it as a root, so what IT needs is on screen before anything is written.
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
            // A declared incompatibility against a mod nobody has is information, not an obstacle.
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
            // Both ids null and only a file_name. Not resolvable through the API at all - the admin has
            // to know, and the rest of the plan still has to work.
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
            // Modrinth may add a type at any time. Guessing could install something the admin never
            // saw, which is the one thing this flow exists to prevent.
            var client = new FakeModrinthClient();
            client.AddMod("PA", "v-a", "A", "a.jar",
                dependencies: new ModrinthDependency { ProjectId = "PB", DependencyType = "something-new" });
            client.AddMod("PB", "v-b", "B", "b.jar");

            var result = await ResolveAsync(client, Request(["v-a"]));

            await Assert.That(result.Nodes.Count).IsEqualTo(1);
        }

        // ---- against what is already installed ---------------------------------------------

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
            // Defaults to skip, never to replace: an upgrade is a deliberate act, and (ServerId,
            // FileName) is unique so a blind insert would conflict anyway.
            var client = new FakeModrinthClient();
            client.AddMod("PA", "v-a-new", "A", "a-new.jar");

            var result = await ResolveAsync(client, Request(
                ["v-a-new"], new InstalledMod("PA", "v-a-old", "a-old.jar")));

            await Assert.That(result.Nodes.Single().Status).IsEqualTo(PlanNodeStatus.OtherVersionInstalled);
        }

        [Test]
        public async Task Resolve_FileNameTakenByAHandUploadedJar_IsMarkedFileNameTaken()
        {
            // The existing row is Manual, so it has no project id to match on - only the filename
            // collides, and that is exactly the case an admin most needs told about.
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

        // ---- budget and batching -----------------------------------------------------------

        [Test]
        public async Task Resolve_HugeDependencyTree_IsRefusedRatherThanWalked()
        {
            // A pathological or hostile graph must not make HOPPER loop against Modrinth on an admin's
            // behalf. Pinned dependencies are used so the whole level arrives in one bulk call and it
            // is the NODE cap that trips, not the call cap.
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
            // The whole reason the walk is batched. Five pinned dependencies at one level must be one
            // /versions call, not five.
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

            // One call for the roots, one for the five pinned dependencies.
            await Assert.That(client.Calls.Count(c => c.StartsWith("versions:"))).IsEqualTo(2);
        }

        [Test]
        public async Task Resolve_VersionModrinthNoLongerHas_IsReportedRatherThanSilentlyDropped()
        {
            // The bulk endpoint drops unknown ids silently instead of answering 404, so comparing
            // counts is the only way to notice.
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
            // Install resolves nothing further, so whatever is missing here can never be recovered.
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
