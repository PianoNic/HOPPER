using HOPPER.Application;
using HOPPER.Application.Exports;
using HOPPER.Application.Loaders;
using HOPPER.Application.Modrinth;
using HOPPER.Domain.Enums;

namespace HOPPER.Tests.Application
{
    public class LoaderDescriptorTests
    {
        private static IEnumerable<ModLoader> Real =>
            Enum.GetValues<ModLoader>().Where(l => l != ModLoader.Unknown);

        [Test]
        public async Task EveryLoaderInTheEnumHasARow()
        {
            foreach (var loader in Real)
                await Assert.That(LoaderDescriptors.For(loader)).IsNotNull();
        }

        [Test]
        public async Task EveryRowRoundTripsThroughAllThreeExternalSpellings()
        {
            foreach (var loader in Real)
            {
                await Assert.That(LoaderIds.FromMrpackKey(LoaderIds.MrpackKey(loader))).IsEqualTo(loader);
                await Assert.That(LoaderIds.FromPrismUid(LoaderIds.PrismUid(loader))).IsEqualTo(loader);
                await Assert.That(LoaderIds.FromCurseForgeId(LoaderIds.CurseForgePrefix(loader) + "-1.2.3")).IsEqualTo(loader);
            }
        }

        [Test]
        public async Task EveryLoaderIsAFacetModrinthWillBeAskedFor()
        {
            foreach (var loader in Real)
            {
                var facet = ServerPlatform.LoaderFacet(loader);

                await Assert.That(ModrinthFacets.KnownLoaders).Contains(facet);
                await Assert.That(ModrinthFacets.ValidateLoader(facet)).IsEqualTo(facet);
            }
        }

        [Test]
        public async Task NoTwoLoadersShareASpelling()
        {
            var rows = LoaderDescriptors.Known;

            await Assert.That(rows.Select(r => r.MrpackKey).Distinct().Count()).IsEqualTo(rows.Count);
            await Assert.That(rows.Select(r => r.PrismUid).Distinct().Count()).IsEqualTo(rows.Count);
            await Assert.That(rows.Select(r => r.CurseForgePrefix).Distinct().Count()).IsEqualTo(rows.Count);
            await Assert.That(rows.Select(r => r.ModrinthFacet).Distinct().Count()).IsEqualTo(rows.Count);
        }

        [Test]
        public async Task AnUnsetLoaderIsRefusedRatherThanGuessed()
        {
            await Assert.That(() => LoaderIds.MrpackKey(ModLoader.Unknown))
                .Throws<ServerPlatformNotConfiguredException>();

            await Assert.That(() => ServerPlatform.LoaderFacet(ModLoader.Unknown))
                .Throws<ServerPlatformNotConfiguredException>();
        }
    }
}
