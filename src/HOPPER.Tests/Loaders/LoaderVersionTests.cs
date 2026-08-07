using HOPPER.Application.Loaders;

namespace HOPPER.Tests.Loaders
{
    public class LoaderVersionTests
    {
        // NeoForge encodes the Minecraft version in its own build number, so getting this wrong
        // offers a 1.21.1 server the builds of a different Minecraft entirely.
        [Test]
        [Arguments("1.21.1", "21.1.")]
        [Arguments("1.21", "21.0.")]
        [Arguments("1.20.4", "20.4.")]
        public async Task NeoForgePrefix_MapsAMinecraftVersionToItsBuildLine(string minecraft, string expected)
        {
            await Assert.That(LoaderVersionClient.NeoForgePrefix(minecraft)).IsEqualTo(expected);
        }

        [Test]
        [Arguments(null)]
        [Arguments("")]
        [Arguments("26.2")]
        public async Task NeoForgePrefix_GivesUpOnAnythingItCannotRead(string? minecraft)
        {
            await Assert.That(LoaderVersionClient.NeoForgePrefix(minecraft)).IsNull();
        }

        // Quilt's newest published entry is routinely a beta, and recommending one is worse than
        // offering a slightly older build.
        [Test]
        [Arguments("0.30.1-beta.2", false)]
        [Arguments("0.30.0", true)]
        [Arguments("21.1.248", true)]
        [Arguments("21.2.0-beta", false)]
        [Arguments("1.0.0-alpha.3", false)]
        [Arguments("2.0.0-rc.1", false)]
        [Arguments("1.19-pre1", false)]
        public async Task IsStable_KeepsOnlyWhatIsNotAPrerelease(string version, bool expected)
        {
            await Assert.That(LoaderVersionClient.IsStable(version)).IsEqualTo(expected);
        }
    }
}
