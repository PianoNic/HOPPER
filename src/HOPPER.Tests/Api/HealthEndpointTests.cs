using System.Net;

namespace HOPPER.Tests.Api
{
    public class HealthEndpointTests
    {
        [Test]
        public async Task Live_IsAnonymousAndDoesNotTouchTheDatabase()
        {
            using var http = HopperApi.Anonymous();

            var response = await http.GetAsync("/health/live");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await response.Content.ReadAsStringAsync()).IsEqualTo("Healthy");
        }

        [Test]
        public async Task Ready_IsAnonymousAndReportsHealthyAgainstARealPostgres()
        {
            using var http = HopperApi.Anonymous();

            var response = await http.GetAsync("/health/ready");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
        }

        // In production the fallback answers 200 with index.html for anything unmatched, so a probe
        // pointed one character off would report a wedged instance as healthy. Only the two exact
        // paths may answer 200.
        [Test]
        public async Task AMisspeltProbePath_IsNotAnswered200()
        {
            using var http = HopperApi.Anonymous();

            var response = await http.GetAsync("/health");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }
    }
}
