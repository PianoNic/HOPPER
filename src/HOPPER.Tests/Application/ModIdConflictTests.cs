using HOPPER.Domain;
using HOPPER.Application;
using HOPPER.Domain.Enums;

namespace HOPPER.Tests.Application
{
    public class ModIdConflictTests
    {
        [Test]
        [Arguments(ModSide.Both, ModSide.Both)]
        [Arguments(ModSide.Both, ModSide.ClientOnly)]
        [Arguments(ModSide.Both, ModSide.ServerOnly)]
        [Arguments(ModSide.ClientOnly, ModSide.Both)]
        [Arguments(ModSide.ServerOnly, ModSide.Both)]
        [Arguments(ModSide.ClientOnly, ModSide.ClientOnly)]
        [Arguments(ModSide.ServerOnly, ModSide.ServerOnly)]
        public async Task TwoCopiesOfOneModIdClashWheneverAnySideGetsBoth(ModSide a, ModSide b)
        {
            await Assert.That(ModSideRules.SharedSide(a, b)).IsNotNull();
        }

        [Test]
        [Arguments(ModSide.ClientOnly, ModSide.ServerOnly)]
        [Arguments(ModSide.ServerOnly, ModSide.ClientOnly)]
        public async Task OppositeSidesAreTheOnePairingThatCanCoexist(ModSide a, ModSide b)
        {
            await Assert.That(ModSideRules.SharedSide(a, b)).IsNull();
        }

        [Test]
        public async Task TheClashNamesTheSideThatWouldGetBoth()
        {
            // The message has to say which machine breaks, because that is what tells an admin which
            // of the two to re-side.
            await Assert.That(ModSideRules.SharedSide(ModSide.Both, ModSide.ClientOnly))
                .IsEqualTo(SyncSide.Client);

            await Assert.That(ModSideRules.SharedSide(ModSide.ServerOnly, ModSide.Both))
                .IsEqualTo(SyncSide.Server);
        }

        private static Mod Jar(string fileName, ModSide side, params string[] modIds) =>
            new()
            {
                Id = Guid.NewGuid(),
                ServerId = Guid.Empty,
                FileName = fileName,
                Sha256 = new string('0', 64),
                Size = 1,
                Side = side,
                ModIds = modIds,
            };

        [Test]
        public async Task Collisions_NamesBothJarsOfAPairAndTheSideThatBreaks()
        {
            var jei = Jar("jei.jar", ModSide.Both, "jei");
            var alsoJei = Jar("jei-renamed.jar", ModSide.ClientOnly, "jei");
            var jade = Jar("jade.jar", ModSide.Both, "jade");

            var found = ModIdConflictValidator.Collisions([jei, alsoJei, jade]);

            await Assert.That(found[jei.Id]).IsEqualTo(SyncSide.Client);
            await Assert.That(found[alsoJei.Id]).IsEqualTo(SyncSide.Client);
            await Assert.That(found.ContainsKey(jade.Id)).IsFalse();
        }

        [Test]
        public async Task Collisions_LeavesTheOneLegalPairingAlone()
        {
            var found = ModIdConflictValidator.Collisions([
                Jar("a.jar", ModSide.ClientOnly, "shared"),
                Jar("b.jar", ModSide.ServerOnly, "shared"),
            ]);

            await Assert.That(found).IsEmpty();
        }

        [Test]
        public async Task Collisions_IgnoresJarsWhoseModIdsWereNeverRead()
        {
            var unread = new Mod
            {
                Id = Guid.NewGuid(),
                ServerId = Guid.Empty,
                FileName = "unread.jar",
                Sha256 = new string('0', 64),
                Size = 1,
                Side = ModSide.Both,
                ModIds = null,
            };

            var found = ModIdConflictValidator.Collisions([unread, Jar("known.jar", ModSide.Both, "jei")]);

            await Assert.That(found).IsEmpty();
        }

        [Test]
        public async Task TheRuleIsSymmetric()
        {
            foreach (var a in Enum.GetValues<ModSide>())
            {
                foreach (var b in Enum.GetValues<ModSide>())
                {
                    await Assert.That(ModSideRules.SharedSide(a, b) is null)
                        .IsEqualTo(ModSideRules.SharedSide(b, a) is null);
                }
            }
        }
    }
}
