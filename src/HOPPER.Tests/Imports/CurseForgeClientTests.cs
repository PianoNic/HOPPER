using System.Net;
using System.Text.Json;
using HOPPER.Application.Imports;
using Microsoft.Extensions.Configuration;

namespace HOPPER.Tests.Imports
{
    public class CurseForgeClientTests
    {
        private const string CurseForgeUrl = "https://api.curseforge.com/v1/mods/files";
        private const string ModrinthUrl = "https://api.modrinth.com/v2/version_files";
        private const string Sha1 = "c5043f862be7db76892c7c0c95d02fa3f8332af0";

        private const string OneFile = """
            {"data":[{"id":6366217,"modId":351491,"fileName":"jei-1.20.1-15.3.0.4.jar",
              "displayName":"JEI 15.3.0.4","downloadUrl":"https://edge.forgecdn.net/files/6366/217/jei.jar",
              "fileLength":1234567,
              "hashes":[{"value":"d41d8cd98f00b204e9800998ecf8427e","algo":2},
                        {"value":"c5043f862be7db76892c7c0c95d02fa3f8332af0","algo":1}]}]}
            """;

        private static CurseForgeClient Client(CannedHttp http, string? apiKey = "test-key")
        {
            var settings = new Dictionary<string, string?>();
            if (apiKey is not null)
                settings["CurseForge:ApiKey"] = apiKey;

            return new CurseForgeClient(http, new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        }

        private static int IdCountIn(string body) =>
            JsonDocument.Parse(body).RootElement.GetProperty("fileIds").GetArrayLength();

        [Test]
        public async Task Resolve_WithAKey_PostsTheFileIdsToCurseForgeWithTheApiKeyHeader()
        {
            var http = new CannedHttp((_, _) => CannedHttp.Ok("""{"data":[]}"""));

            await Client(http).ResolveAsync([6366217, 4938351], CancellationToken.None);

            var call = http.Calls.Single();
            await Assert.That(call.Url).IsEqualTo(CurseForgeUrl);
            await Assert.That(call.ApiKey).IsEqualTo("test-key");
            await Assert.That(call.Body).Contains("6366217");
            await Assert.That(call.Body).Contains("4938351");
        }

        [Test]
        public async Task Resolve_ParsesEveryFieldThePlannerLaterReadsOn()
        {
            var http = new CannedHttp((_, _) => CannedHttp.Ok(OneFile));

            var resolved = await Client(http).ResolveAsync([6366217], CancellationToken.None);
            var file = resolved[6366217];

            await Assert.That(file.ProjectId).IsEqualTo(351491);
            await Assert.That(file.FileId).IsEqualTo(6366217);
            await Assert.That(file.FileName).IsEqualTo("jei-1.20.1-15.3.0.4.jar");
            await Assert.That(file.DownloadUrl!.Host).IsEqualTo("edge.forgecdn.net");
            await Assert.That(file.Length).IsEqualTo(1234567L);
            await Assert.That(file.DisplayName).IsEqualTo("JEI 15.3.0.4");
        }

        [Test]
        public async Task Resolve_Sha1_IsTheAlgoOneHashNotWhicheverIsFirst()
        {
            var http = new CannedHttp((_, _) => CannedHttp.Ok(OneFile));

            var resolved = await Client(http).ResolveAsync([6366217], CancellationToken.None);

            await Assert.That(resolved[6366217].Sha1).IsEqualTo(Sha1);
        }

        [Test]
        public async Task Resolve_AFileWithNoAlgoOneHash_HasNoSha1RatherThanTheMd5()
        {
            var http = new CannedHttp((_, _) => CannedHttp.Ok("""
                {"data":[{"id":1,"modId":2,"fileName":"a.jar","downloadUrl":"https://edge.forgecdn.net/a.jar",
                  "hashes":[{"value":"d41d8cd98f00b204e9800998ecf8427e","algo":2}]}]}
                """));

            var resolved = await Client(http).ResolveAsync([1], CancellationToken.None);

            await Assert.That(resolved[1].Sha1).IsNull();
        }

        [Test]
        public async Task Resolve_MoreThanAHundredIds_IsSplitIntoBatchesOfAHundred()
        {
            var http = new CannedHttp((_, _) => CannedHttp.Ok("""{"data":[]}"""));

            await Client(http).ResolveAsync([.. Enumerable.Range(1, 150)], CancellationToken.None);

            await Assert.That(http.Calls).Count().IsEqualTo(2);
            await Assert.That(IdCountIn(http.Calls[0].Body)).IsEqualTo(100);
            await Assert.That(IdCountIn(http.Calls[1].Body)).IsEqualTo(50);
        }

        [Test]
        public async Task Resolve_DuplicateIds_AreAskedForOnce()
        {
            var http = new CannedHttp((_, _) => CannedHttp.Ok("""{"data":[]}"""));

            await Client(http).ResolveAsync([7, 7, 7, 8], CancellationToken.None);

            await Assert.That(IdCountIn(http.Calls.Single().Body)).IsEqualTo(2);
        }

        [Test]
        public async Task Resolve_ANonSuccessResponse_YieldsNothingRatherThanThrowing()
        {
            var http = new CannedHttp((_, _) => CannedHttp.Json(HttpStatusCode.Forbidden, """{"error":"bad key"}"""));

            var resolved = await Client(http).ResolveAsync([6366217], CancellationToken.None);

            await Assert.That(resolved).IsEmpty();
            await Assert.That(http.Calls).Count().IsEqualTo(1);
        }

        [Test]
        public async Task Resolve_WithoutAKey_MakesNoHttpCallAtAll()
        {
            var http = new CannedHttp((_, _) => throw new InvalidOperationException("must not be called"));
            var client = Client(http, apiKey: null);

            var resolved = await client.ResolveAsync([6366217], CancellationToken.None);

            await Assert.That(client.IsConfigured).IsFalse();
            await Assert.That(resolved).IsEmpty();
            await Assert.That(http.Calls).IsEmpty();
        }

        [Test]
        public async Task Resolve_AnEmptyIdList_MakesNoHttpCall()
        {
            var http = new CannedHttp((_, _) => throw new InvalidOperationException("must not be called"));

            var resolved = await Client(http).ResolveAsync([], CancellationToken.None);

            await Assert.That(resolved).IsEmpty();
            await Assert.That(http.Calls).IsEmpty();
        }

        [Test]
        public async Task FindOnModrinthBySha1_AKnownHash_ReturnsTheCdnUrl()
        {
            var http = new CannedHttp((_, _) => CannedHttp.Ok(
                "{\"" + Sha1 + "\":{\"files\":[{\"hashes\":{\"sha1\":\"" + Sha1 + "\"}"
                + ",\"url\":\"https://cdn.modrinth.com/data/u6dRKJwZ/versions/x/jei.jar\"}]}}"));

            var url = await Client(http).FindOnModrinthBySha1Async(Sha1, CancellationToken.None);

            await Assert.That(url!.Host).IsEqualTo("cdn.modrinth.com");

            var call = http.Calls.Single();
            await Assert.That(call.Url).IsEqualTo(ModrinthUrl);
            await Assert.That(call.Body).Contains("\"algorithm\":\"sha1\"");
            await Assert.That(call.Body).Contains(Sha1);
        }

        [Test]
        public async Task FindOnModrinthBySha1_AnUnknownHash_ReturnsNull()
        {
            var empty = new CannedHttp((_, _) => CannedHttp.Ok("{}"));
            var missing = new CannedHttp((_, _) => CannedHttp.Json(HttpStatusCode.NotFound, "{}"));

            await Assert.That(await Client(empty).FindOnModrinthBySha1Async(Sha1, CancellationToken.None)).IsNull();
            await Assert.That(await Client(missing).FindOnModrinthBySha1Async(Sha1, CancellationToken.None)).IsNull();
        }

        [Test]
        public async Task FindOnModrinthBySha1_ATransportFailure_ReturnsNullRatherThanFailingTheImport()
        {
            var http = new CannedHttp((_, _) => throw new HttpRequestException("connection reset"));

            await Assert.That(await Client(http).FindOnModrinthBySha1Async(Sha1, CancellationToken.None)).IsNull();
        }

        [Test]
        public async Task FindOnModrinthBySha1_ABlankHash_MakesNoHttpCall()
        {
            var http = new CannedHttp((_, _) => throw new InvalidOperationException("must not be called"));

            await Assert.That(await Client(http).FindOnModrinthBySha1Async("  ", CancellationToken.None)).IsNull();
            await Assert.That(http.Calls).IsEmpty();
        }
    }
}
