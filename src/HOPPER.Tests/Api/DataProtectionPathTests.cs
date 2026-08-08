using HOPPER.API.Extensions;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Tests.Api
{
    public class DataProtectionPathTests
    {
        private static IConfiguration Config(params (string Key, string? Value)[] values) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => v.Value))
                .Build();

        [Test]
        public async Task Keys_SitBesideTheBlobsRatherThanInsideThem()
        {
            var blobs = Path.Combine(Path.GetTempPath(), "hopper-keys-test", "blobs");

            var directory = DataProtectionExtensions.KeyDirectory(Config(("Blobs:Directory", blobs)));

            await Assert.That(Path.GetFileName(directory)).IsEqualTo("keys");

            // Inside the blob root is the one place they must not go: the reclaim sweep owns it.
            await Assert.That(Path.GetFullPath(directory).StartsWith(Path.GetFullPath(blobs), StringComparison.Ordinal))
                .IsFalse();

            await Assert.That(Path.GetFullPath(Path.GetDirectoryName(directory)!))
                .IsEqualTo(Path.GetFullPath(Path.GetDirectoryName(blobs)!));
        }

        [Test]
        public async Task Keys_CanBePutSomewhereElseOutright()
        {
            var directory = DataProtectionExtensions.KeyDirectory(
                Config(("DataProtection:Directory", "/mnt/keys")));

            await Assert.That(directory).IsEqualTo("/mnt/keys");
        }

        [Test]
        public async Task Keys_AreNotUnderTheBlobRootEvenWhenItHasATrailingSeparator()
        {
            var root = Path.Combine(Path.GetTempPath(), "hopper-keys-test");
            var blobs = Path.Combine(root, "blobs") + Path.DirectorySeparatorChar;

            var directory = DataProtectionExtensions.KeyDirectory(Config(("Blobs:Directory", blobs)));

            // Without trimming the separator this lands inside the blob root instead of beside it.
            await Assert.That(Path.GetFullPath(directory)).IsEqualTo(Path.GetFullPath(Path.Combine(root, "keys")));
        }
    }
}
