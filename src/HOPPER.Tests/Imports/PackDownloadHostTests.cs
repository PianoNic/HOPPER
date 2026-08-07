using HOPPER.Application.Imports;

namespace HOPPER.Tests.Imports
{
    public class PackDownloadHostTests
    {
        [Test]
        public async Task Defaults_ContainBothForgeCdnHosts()
        {
            var allowed = PackDownloadHosts.Allowed(TestLimits.Config);

            await Assert.That(allowed.Contains("edge.forgecdn.net")).IsTrue();
            await Assert.That(allowed.Contains("mediafilez.forgecdn.net")).IsTrue();
        }

        [Test]
        public async Task Defaults_StillContainTheModrinthCdn()
        {
            var allowed = PackDownloadHosts.Allowed(TestLimits.Config);

            await Assert.That(allowed.Contains("cdn.modrinth.com")).IsTrue();
        }

        [Test]
        public async Task Hosts_AreMatchedWithoutRegardToCase()
        {
            var allowed = PackDownloadHosts.Allowed(TestLimits.Config);

            await Assert.That(allowed.Contains("EDGE.ForgeCdn.NET")).IsTrue();
        }

        [Test]
        public async Task ConfiguredHosts_ReplaceTheDefaultsRatherThanAddingToThem()
        {
            var allowed = PackDownloadHosts.Allowed(
                TestLimits.ConfigWith(("Hopper:PackDownloadHosts:0", "cdn.modrinth.com")));

            await Assert.That(allowed).Count().IsEqualTo(1);
            await Assert.That(allowed.Contains("cdn.modrinth.com")).IsTrue();
            await Assert.That(allowed.Contains("edge.forgecdn.net")).IsFalse();
        }
    }
}
