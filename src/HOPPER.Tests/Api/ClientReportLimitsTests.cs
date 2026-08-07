using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace HOPPER.Tests.Api
{
    public class ClientReportLimitsTests
    {
        private const string JarSha = "817c44afc9bd5ffa653785e54107b708af0a1b5b695095e0a5235cfe7b24b4f3";

        private static object Report(string clientId, params (string File, string Sha)[] mods) => new
        {
            clientId,
            username = "alex",
            mods = mods.Select(m => new { file = m.File, sha256 = m.Sha }).ToArray(),
        };

        [Test]
        public async Task Report_OversizedClientId_Is400NotA500()
        {
            using var http = HopperApi.AsGameClient();

            var response = await http.PostAsJsonAsync("/api/clients/report",
                Report(new string('c', 4000), ("jei.jar", JarSha)));

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }

        [Test]
        public async Task Report_Sha256ThatIsNotAHash_Is400()
        {
            using var http = HopperApi.AsGameClient();

            var response = await http.PostAsJsonAsync("/api/clients/report",
                Report("limits-badhash-" + Guid.NewGuid().ToString("N")[..8], ("jei.jar", "aa")));

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }

        [Test]
        public async Task Report_BodyOverTheRequestSizeLimit_IsRejected()
        {
            using var http = HopperApi.AsGameClient();

            var padding = new string('a', 2 * 1024 * 1024);
            using var content = new StringContent(
                $$"""{"clientId":"{{padding}}","username":null,"mods":[]}""", Encoding.UTF8, "application/json");

            var response = await http.PostAsync("/api/clients/report", content);

            await Assert.That((int)response.StatusCode).IsGreaterThanOrEqualTo(400);
            await Assert.That(response.StatusCode).IsNotEqualTo(HttpStatusCode.NoContent);
        }

        [Test]
        public async Task Report_AWellFormedFourHundredModReport_StillSucceeds()
        {
            using var http = HopperApi.AsGameClient();

            var mods = Enumerable.Range(0, 400).Select(i => ($"limits-mod-{i}.jar", JarSha)).ToArray();

            var response = await http.PostAsJsonAsync("/api/clients/report",
                Report("limits-bigpack-" + Guid.NewGuid().ToString("N")[..8], mods));

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        }

        [Test]
        public async Task Report_ANormalReport_StillSucceeds()
        {
            using var http = HopperApi.AsGameClient();

            var response = await http.PostAsJsonAsync("/api/clients/report",
                Report("limits-normal-" + Guid.NewGuid().ToString("N")[..8], ("jei.jar", JarSha)));

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        }
    }
}
