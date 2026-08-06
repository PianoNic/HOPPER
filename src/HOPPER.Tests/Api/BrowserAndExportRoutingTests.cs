using System.Net;
using HOPPER.Application.Exports;
using HOPPER.Application.Modrinth;
using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Tests.Api
{
    public class BrowserAndExportRoutingTests
    {
        private static string PlanPath => $"/api/servers/{HopperApi.ServerAId}/modrinth/plan";

        private static string InstallPath => $"/api/servers/{HopperApi.ServerAId}/modrinth/install";

        private static string ExportPath => $"/api/servers/{HopperApi.ServerAId}/export";

        [Test]
        [Arguments("/api/modrinth/search?query=jei")]
        [Arguments("/api/modrinth/projects/jei")]
        [Arguments("/api/modrinth/projects/jei/versions")]
        [Arguments("/api/modrinth/tags")]
        public async Task BrowserRoute_NoToken_Is401(string path)
        {
            using var http = HopperApi.Anonymous();

            var response = await http.GetAsync(path);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        [Test]
        public async Task ExportRoute_NoToken_Is401()
        {
            using var http = HopperApi.Anonymous();

            var response = await http.GetAsync(ExportPath + "?format=1");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        [Test]
        [Arguments("plan")]
        [Arguments("install")]
        public async Task ServerModrinthRoute_NoToken_Is401(string action)
        {
            using var http = HopperApi.Anonymous();

            var response = await http.PostAsync(
                $"/api/servers/{HopperApi.ServerAId}/modrinth/{action}",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        [Test]
        [Arguments("plan")]
        [Arguments("install")]
        public async Task ServerModrinthRoute_ClientToken_Is401(string action)
        {
            using var http = HopperApi.AsGameClient();

            var response = await http.PostAsync(
                $"/api/servers/{HopperApi.ServerAId}/modrinth/{action}",
                new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        [Test]
        public async Task ExportRoute_ClientToken_Is401()
        {
            using var http = HopperApi.AsGameClient();

            var response = await http.GetAsync(ExportPath + "?format=1");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        [Test]
        public async Task Container_ResolvesEverythingTheBrowserAndTheExportersNeed()
        {
            using var scope = HopperApi.Services.CreateScope();

            await Assert.That(scope.ServiceProvider.GetService<IModrinthClient>()).IsNotNull();
            await Assert.That(scope.ServiceProvider.GetService<IModrinthDependencyResolver>()).IsNotNull();

            var exporters = scope.ServiceProvider.GetServices<IPackExporter>().ToList();

            await Assert.That(exporters.Select(e => e.Format).Order().ToList())
                .IsEquivalentTo(new[]
                {
                    HOPPER.Domain.Enums.PackFormat.Modrinth,
                    HOPPER.Domain.Enums.PackFormat.CurseForge,
                    HOPPER.Domain.Enums.PackFormat.PrismInstance,
                });
        }

        [Test]
        public async Task RateLimiter_IsASingleton()
        {
            using var first = HopperApi.Services.CreateScope();
            using var second = HopperApi.Services.CreateScope();

            var a = first.ServiceProvider.GetRequiredService<ModrinthRateLimiter>();
            var b = second.ServiceProvider.GetRequiredService<ModrinthRateLimiter>();

            await Assert.That(ReferenceEquals(a, b)).IsTrue();
        }

        [Test]
        public async Task ModrinthHttpClient_CarriesTheDescriptiveUserAgentModrinthRequire()
        {
            var factory = HopperApi.Services.GetRequiredService<IHttpClientFactory>();

            using var modrinth = factory.CreateClient(ModrinthHttpClients.Modrinth);

            await Assert.That(modrinth.DefaultRequestHeaders.TryGetValues("User-Agent", out var agents)).IsTrue();
            var agent = string.Join(' ', agents!);

            await Assert.That(agent).StartsWith("PianoNic/HOPPER/");
            await Assert.That(agent).Contains("(github.com/PianoNic/HOPPER)");
            await Assert.That(modrinth.BaseAddress!.ToString()).IsEqualTo("https://api.modrinth.com/v2/");

            using var packs = factory.CreateClient(HOPPER.Application.Imports.ImportHttpClients.Packs);

            await Assert.That(packs.DefaultRequestHeaders.TryGetValues("User-Agent", out var packAgents)).IsTrue();
            await Assert.That(string.Join(' ', packAgents!)).StartsWith("PianoNic/HOPPER/");
        }
    }
}
