using System.Net;

namespace HOPPER.Tests.Api
{
    public class AuthSplitTests
    {
        private static string ModsPath => $"/api/servers/{HopperApi.ServerAId}/mods";

        private static string ClientsPath => $"/api/servers/{HopperApi.ServerAId}/clients";

        [Test]
        [Arguments("/api/manifest")]
        [Arguments("/api/blobs/" + "0000000000000000000000000000000000000000000000000000000000000000")]
        public async Task ClientEndpoint_NoToken_Is401(string path)
        {
            using var http = HopperApi.Anonymous();

            var response = await http.GetAsync(path);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        [Test]
        public async Task ClientReport_NoToken_Is401()
        {
            using var http = HopperApi.Anonymous();

            var response = await http.PostAsync("/api/clients/report",
                new StringContent("""{"clientId":"c","username":null,"mods":[]}""", System.Text.Encoding.UTF8, "application/json"));

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        [Test]
        public async Task AdminMods_NoToken_Is401()
        {
            using var http = HopperApi.Anonymous();

            var response = await http.GetAsync(ModsPath);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        [Test]
        public async Task AdminClients_NoToken_Is401()
        {
            using var http = HopperApi.Anonymous();

            var response = await http.GetAsync(ClientsPath);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        [Test]
        public async Task AdminMods_ValidClientToken_Is401()
        {
            using var http = HopperApi.AsGameClient();

            var response = await http.GetAsync(ModsPath);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        [Test]
        public async Task AdminClients_ValidClientToken_Is401()
        {
            using var http = HopperApi.AsGameClient();

            var response = await http.GetAsync(ClientsPath);

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        [Test]
        public async Task ClientEndpoint_TokenMatchingNoServer_Is401()
        {
            using var http = HopperApi.WithBearer("not-any-server-token");

            var response = await http.GetAsync("/api/manifest");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        [Test]
        public async Task ClientEndpoint_TokenThatSharesAPrefix_Is401()
        {
            using var http = HopperApi.WithBearer(HopperApi.ClientToken[..^1]);

            var response = await http.GetAsync("/api/manifest");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        [Test]
        public async Task ClientEndpoint_ValidClientToken_IsAllowed()
        {
            using var http = HopperApi.AsGameClient();

            var response = await http.GetAsync("/api/manifest");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        [Test]
        public async Task AppEndpoint_IsAnonymous()
        {
            using var http = HopperApi.Anonymous();

            var response = await http.GetAsync("/api/app");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
    }
}
