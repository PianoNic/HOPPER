using System.Net;
using HOPPER.Application.Exports;
using HOPPER.Application.Modrinth;
using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Tests.Api
{
    /// <summary>
    /// Boots the real host and asserts the things only the real host can answer: that the new routes
    /// exist, that they inherit the fallback policy rather than being accidentally anonymous, and that
    /// everything the browser and the exporters need can actually be resolved out of the container.
    ///
    /// A missing DI registration is invisible to every unit test in this suite - the resolver and the
    /// exporters are constructed by hand there - and shows up in production as a 500 on the first
    /// click. This is the cheapest place to catch it.
    ///
    /// No route here is exercised against the live Modrinth API: a 401 is decided by the
    /// authorization middleware, long before a handler could open a socket.
    /// </summary>
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
            // Protected by writing no [Authorize] attribute at all - the fallback policy covers it.
            // A route that answered 200 here would be one an unauthenticated caller could use HOPPER
            // to proxy requests to Modrinth with.
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
            // The other direction of the auth split. A client token sits in plain text in a jar on a
            // player's machine; it must not reach anything that adds mods to a server.
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
            // The registrations AddModrinth() and AddPackExports() make. Resolving them here is what
            // turns "I added a line to Program.cs" into something asserted.
            using var scope = HopperApi.Services.CreateScope();

            await Assert.That(scope.ServiceProvider.GetService<IModrinthClient>()).IsNotNull();
            await Assert.That(scope.ServiceProvider.GetService<IModrinthDependencyResolver>()).IsNotNull();

            var exporters = scope.ServiceProvider.GetServices<IPackExporter>().ToList();

            // One per writable format. A duplicate or a missing one would make the export route pick
            // silently by registration order instead of by format.
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
            // Modrinth enforce their limit per IP, and this process has one. A scoped limiter would be
            // a fresh 300-token budget per request, which is no limiter at all.
            using var first = HopperApi.Services.CreateScope();
            using var second = HopperApi.Services.CreateScope();

            var a = first.ServiceProvider.GetRequiredService<ModrinthRateLimiter>();
            var b = second.ServiceProvider.GetRequiredService<ModrinthRateLimiter>();

            await Assert.That(ReferenceEquals(a, b)).IsTrue();
        }

        [Test]
        public async Task ModrinthHttpClient_CarriesTheDescriptiveUserAgentModrinthRequire()
        {
            // Modrinth document that a generic agent raises the likelihood of being blocked, and the
            // blocking is reputation-based and applied later - so a passing live request proves
            // nothing and this is the only place the header can actually be asserted.
            //
            // Read back untyped, because it is SET untyped: "PianoNic/HOPPER/1.0.0" carries two
            // slashes where RFC 7230 allows a product token one, so the strongly typed collection
            // refuses to parse it. Merely creating these clients is half the assertion - the validated
            // path throws FormatException at construction, which would take the pack importer down
            // with it.
            var factory = HopperApi.Services.GetRequiredService<IHttpClientFactory>();

            using var modrinth = factory.CreateClient(ModrinthHttpClients.Modrinth);

            await Assert.That(modrinth.DefaultRequestHeaders.TryGetValues("User-Agent", out var agents)).IsTrue();
            var agent = string.Join(' ', agents!);

            await Assert.That(agent).StartsWith("PianoNic/HOPPER/");
            await Assert.That(agent).Contains("(github.com/PianoNic/HOPPER)");
            await Assert.That(modrinth.BaseAddress!.ToString()).IsEqualTo("https://api.modrinth.com/v2/");

            // The pack importer's client talks to api.modrinth.com too, for the sha1 reverse lookup,
            // so it carries the same agent rather than a generic one.
            using var packs = factory.CreateClient(HOPPER.Application.Imports.ImportHttpClients.Packs);

            await Assert.That(packs.DefaultRequestHeaders.TryGetValues("User-Agent", out var packAgents)).IsTrue();
            await Assert.That(string.Join(' ', packAgents!)).StartsWith("PianoNic/HOPPER/");
        }
    }
}
