using System.Net;

namespace HOPPER.Tests.Api
{
    /// <summary>
    /// Two credentials, two surfaces, no overlap. A client token sits in plain text in a properties
    /// file (or a downloaded jar) on machines nobody controls, so it must never open the admin
    /// surface; the admin's OIDC token is not something a jar in a mods folder can ever obtain, so it
    /// must not be the only thing standing between the internet and the mod set either. Both
    /// directions are asserted, because only one of them failing is exactly the kind of thing a
    /// refactor does.
    /// </summary>
    public class AuthSplitTests
    {
        // Built at call time rather than declared as [Arguments]: the admin surface is nested under a
        // server id now, and a Guid is not a compile-time constant.
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
            // A client token is handed to every friend in the group. If it also listed and deleted
            // mods, one leaked properties file would be enough to empty that server's mod set.
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
            // There is no allow-list any more: a token is valid exactly when it resolves to a Server
            // row, and one that resolves to nothing is indistinguishable from one that never existed.
            using var http = HopperApi.WithBearer("not-any-server-token");

            var response = await http.GetAsync("/api/manifest");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        }

        [Test]
        public async Task ClientEndpoint_TokenThatSharesAPrefix_Is401()
        {
            // The comparison is over the raw bytes with a fixed-time compare, so a prefix is not a
            // match and does not get any closer to being one.
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
            // The SPA calls this before it has a token, because this is what tells it where to get
            // one. Putting it behind the fallback policy would deadlock the login flow.
            using var http = HopperApi.Anonymous();

            var response = await http.GetAsync("/api/app");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }
    }
}
