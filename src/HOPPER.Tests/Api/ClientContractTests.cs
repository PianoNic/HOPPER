using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HOPPER.Application.Command.Mods;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Tests.Api
{
    /// <summary>
    /// The three endpoints the shipped Forge locator talks to, exercised over HTTP through the real
    /// pipeline: model binding, the serializer options the app actually runs with, and the exception
    /// middleware. The unit tests in Wire/ pin the DTOs; these pin what comes out of the socket.
    /// </summary>
    public class ClientContractTests
    {
        private static readonly byte[] JarBytes = Encoding.UTF8.GetBytes("PK pretend forge jar payload");
        private static readonly string JarSha = Convert.ToHexStringLower(SHA256.HashData(JarBytes));

        /// <summary>Puts a jar in the mod set. The admin HTTP endpoint is OIDC-gated and this suite
        /// has no IdP, so the seed goes through the same command handler that endpoint calls, against
        /// the running host's own services - the blob store and database the client endpoints then
        /// read from are the ones under test.</summary>
        private static async Task SeedJarAsync(string fileName)
        {
            var serverId = HopperApi.ServerAId;

            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
            if (await db.Mods.AnyAsync(m => m.ServerId == serverId && m.FileName == fileName))
                return;

            var handler = new UploadModsCommandHandler(
                db,
                scope.ServiceProvider.GetRequiredService<IBlobStorage>(),
                scope.ServiceProvider.GetRequiredService<ICurrentUserService>());

            await handler.Handle(
                new UploadModsCommand(serverId, [new UploadFile(fileName, new MemoryStream(JarBytes))]),
                CancellationToken.None);
        }

        [Test]
        public async Task Manifest_OverHttp_MatchesTheShippedWireFormatExactly()
        {
            await SeedJarAsync("contract-check.jar");
            using var http = HopperApi.AsGameClient();

            var response = await http.GetAsync("/api/manifest");
            var body = await response.Content.ReadAsStringAsync();

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);

            using var document = JsonDocument.Parse(body);
            await Assert.That(document.RootElement.EnumerateObject().Select(p => p.Name).ToList())
                .IsEquivalentTo(new[] { "mods" });

            var entry = document.RootElement.GetProperty("mods")
                .EnumerateArray()
                .Single(e => e.GetProperty("file").GetString() == "contract-check.jar");

            await Assert.That(entry.EnumerateObject().Select(p => p.Name).ToList())
                .IsEquivalentTo(new[] { "file", "url", "sha256", "size" });
            await Assert.That(entry.GetProperty("sha256").GetString()).IsEqualTo(JarSha);
            await Assert.That(entry.GetProperty("size").ValueKind).IsEqualTo(JsonValueKind.Number);
            await Assert.That(entry.GetProperty("size").GetInt64()).IsEqualTo((long)JarBytes.Length);
            await Assert.That(entry.GetProperty("url").GetString()!).EndsWith($"/api/blobs/{JarSha}");
        }

        [Test]
        public async Task Manifest_OverHttp_CarriesModIdsForARealJar()
        {
            // The other half of the contract: the entry above carries four fields because its
            // payload is not a zip, and this one carries five because it is a real jar with a real
            // META-INF/mods.toml. The four originals are unchanged in both.
            var jar = ForgeJarBytes("contractmod");
            await SeedBytesAsync("contract-modid.jar", jar);
            using var http = HopperApi.AsGameClient();

            var response = await http.GetAsync("/api/manifest");

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var entry = document.RootElement.GetProperty("mods")
                .EnumerateArray()
                .Single(e => e.GetProperty("file").GetString() == "contract-modid.jar");

            await Assert.That(entry.EnumerateObject().Select(p => p.Name).ToList())
                .IsEquivalentTo(new[] { "file", "url", "sha256", "size", "modIds" });

            var sha = Convert.ToHexStringLower(SHA256.HashData(jar));
            await Assert.That(entry.GetProperty("sha256").GetString()).IsEqualTo(sha);
            await Assert.That(entry.GetProperty("size").GetInt64()).IsEqualTo((long)jar.Length);
            await Assert.That(entry.GetProperty("url").GetString()!).EndsWith($"/api/blobs/{sha}");
            await Assert.That(entry.GetProperty("modIds").EnumerateArray().Select(e => e.GetString()!).ToList())
                .IsEquivalentTo(new[] { "contractmod" });
        }

        private static byte[] ForgeJarBytes(string modId)
        {
            var buffer = new MemoryStream();

            using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
            {
                using var stream = archive.CreateEntry("META-INF/mods.toml").Open();
                stream.Write(Encoding.UTF8.GetBytes($"modLoader=\"javafml\"\n[[mods]]\nmodId=\"{modId}\"\n"));
            }

            return buffer.ToArray();
        }

        private static async Task SeedBytesAsync(string fileName, byte[] bytes)
        {
            var serverId = HopperApi.ServerAId;

            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
            if (await db.Mods.AnyAsync(m => m.ServerId == serverId && m.FileName == fileName))
                return;

            var handler = new UploadModsCommandHandler(
                db,
                scope.ServiceProvider.GetRequiredService<IBlobStorage>(),
                scope.ServiceProvider.GetRequiredService<ICurrentUserService>());

            await handler.Handle(
                new UploadModsCommand(serverId, [new UploadFile(fileName, new MemoryStream(bytes))]),
                CancellationToken.None);
        }

        [Test]
        public async Task Manifest_Url_IsAbsoluteAndFollowsForwardedHeaders()
        {
            // Clients dial this URL from another machine, so a relative or internal one is useless.
            // UseForwardedHeaders runs first precisely so a reverse proxy's scheme and host win.
            await SeedJarAsync("forwarded-check.jar");
            using var http = HopperApi.AsGameClient();

            var request = new HttpRequestMessage(HttpMethod.Get, "/api/manifest");
            request.Headers.Add("X-Forwarded-Proto", "https");
            request.Headers.Add("X-Forwarded-Host", "hopper.example.com");
            var response = await http.SendAsync(request);

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var url = document.RootElement.GetProperty("mods").EnumerateArray().First().GetProperty("url").GetString()!;

            await Assert.That(url).StartsWith("https://hopper.example.com/api/blobs/");
        }

        [Test]
        public async Task Blob_ByContentAddress_StreamsTheExactBytes()
        {
            await SeedJarAsync("blob-check.jar");
            using var http = HopperApi.AsGameClient();

            var response = await http.GetAsync($"/api/blobs/{JarSha}");
            var bytes = await response.Content.ReadAsByteArrayAsync();

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(bytes).IsEquivalentTo(JarBytes);
            // The client re-hashes what it downloaded and discards a mismatch, so this round trip is
            // what decides whether a sync can ever converge.
            await Assert.That(Convert.ToHexStringLower(SHA256.HashData(bytes))).IsEqualTo(JarSha);
        }

        [Test]
        [Arguments("not-a-sha")]
        [Arguments("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
        public async Task Blob_MalformedHash_Is400(string sha)
        {
            using var http = HopperApi.AsGameClient();

            var response = await http.GetAsync($"/api/blobs/{sha}");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }

        [Test]
        public async Task Blob_UnknownHash_Is404()
        {
            using var http = HopperApi.AsGameClient();

            var response = await http.GetAsync($"/api/blobs/{new string('9', 64)}");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }

        [Test]
        public async Task Report_WithNullUsername_Is204AndTheClientIsRecorded()
        {
            // The exact body Syncer.report() sends on a dedicated server. This used to be a 400 from
            // model binding, which Syncer swallowed - the client never appeared and nothing said so.
            using var http = HopperApi.AsGameClient();
            var clientId = "e2e-null-" + Guid.NewGuid().ToString("N");

            var response = await http.PostAsync("/api/clients/report", new StringContent(
                $$"""{"clientId":"{{clientId}}","username":null,"mods":[{"file":"jei.jar","sha256":"{{JarSha}}"}]}""",
                Encoding.UTF8, "application/json"));

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
            var stored = await db.Clients.SingleAsync(c => c.ClientId == clientId);

            await Assert.That(stored.Username).IsNull();
        }

        [Test]
        public async Task Report_WithAUsername_Is204()
        {
            using var http = HopperApi.AsGameClient();

            var response = await http.PostAsJsonAsync("/api/clients/report", new
            {
                clientId = "e2e-named-" + Guid.NewGuid().ToString("N"),
                username = "Alex",
                mods = Array.Empty<object>(),
            });

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);
        }

        [Test]
        public async Task Report_WithoutTheUsernameProperty_Is400()
        {
            // Nullable but still required: the shipped client always sends the property.
            using var http = HopperApi.AsGameClient();

            var response = await http.PostAsync("/api/clients/report", new StringContent(
                """{"clientId":"e2e-missing","mods":[]}""", Encoding.UTF8, "application/json"));

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }

        [Test]
        public async Task Report_WithBlankClientId_Is400()
        {
            using var http = HopperApi.AsGameClient();

            var response = await http.PostAsync("/api/clients/report", new StringContent(
                """{"clientId":"  ","username":null,"mods":[]}""", Encoding.UTF8, "application/json"));

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        }
    }
}
