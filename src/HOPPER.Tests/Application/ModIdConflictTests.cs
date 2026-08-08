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
