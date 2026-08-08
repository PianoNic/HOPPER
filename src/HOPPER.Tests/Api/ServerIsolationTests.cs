using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HOPPER.Application.Command.Mods;
using HOPPER.Domain;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Tests.Api
{
    public class ServerIsolationTests
    {
        private static string BlobRoot =>
            HopperApi.Services.GetRequiredService<IConfiguration>()["Blobs:Directory"]!;

        private static byte[] JarFor(string marker) => Encoding.UTF8.GetBytes($"PK pretend jar {marker}");

        private static string ShaOf(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

        private static async Task SeedAsync(Guid serverId, string fileName, byte[] bytes)
        {
            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();

            var handler = new UploadModsCommandHandler(
                db,
                scope.ServiceProvider.GetRequiredService<IBlobStorage>(),
                scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
                scope.ServiceProvider.GetRequiredService<IConfiguration>());

            await handler.Handle(
                new UploadModsCommand(serverId, [new UploadFile(fileName, new MemoryStream(bytes))]),
                CancellationToken.None);
        }

        private static async Task<List<string>> ManifestFilesAsync(HttpClient http)
        {
            using var document = JsonDocument.Parse(await http.GetStringAsync("/api/manifest"));
            return document.RootElement.GetProperty("mods")
                .EnumerateArray()
                .Select(e => e.GetProperty("file").GetString()!)
                .ToList();
        }

        [Test]
        public async Task Manifest_ShowsOnlyTheCallersOwnServersMods()
        {
            var onlyOnA = "isolation-only-a-" + Guid.NewGuid().ToString("N")[..8] + ".jar";
            var onlyOnB = "isolation-only-b-" + Guid.NewGuid().ToString("N")[..8] + ".jar";

            await SeedAsync(HopperApi.ServerAId, onlyOnA, JarFor(onlyOnA));
            await SeedAsync(HopperApi.ServerBId, onlyOnB, JarFor(onlyOnB));

            using var a = HopperApi.AsGameClient();
            using var b = HopperApi.AsGameClientB();

            var fromA = await ManifestFilesAsync(a);
            var fromB = await ManifestFilesAsync(b);

            await Assert.That(fromA).Contains(onlyOnA);
            await Assert.That(fromA).DoesNotContain(onlyOnB);
            await Assert.That(fromB).Contains(onlyOnB);
            await Assert.That(fromB).DoesNotContain(onlyOnA);
        }

        [Test]
        public async Task Blob_ThatBelongsToAnotherServer_Is404NotAForbidden()
        {
            var fileName = "isolation-blob-" + Guid.NewGuid().ToString("N")[..8] + ".jar";
            var bytes = JarFor(fileName);
            await SeedAsync(HopperApi.ServerAId, fileName, bytes);

            using var a = HopperApi.AsGameClient();
            using var b = HopperApi.AsGameClientB();

            var mine = await a.GetAsync($"/api/blobs/{ShaOf(bytes)}");
            var theirs = await b.GetAsync($"/api/blobs/{ShaOf(bytes)}");

            await Assert.That(mine.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(theirs.StatusCode).IsEqualTo(HttpStatusCode.NotFound);
        }

        [Test]
        public async Task SameJarOnTwoServers_IsTwoRowsAndOneFileOnDisk()
        {
            var fileName = "isolation-shared-" + Guid.NewGuid().ToString("N")[..8] + ".jar";
            var bytes = JarFor(fileName);
            var sha = ShaOf(bytes);

            await SeedAsync(HopperApi.ServerAId, fileName, bytes);
            await SeedAsync(HopperApi.ServerBId, fileName, bytes);

            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();

            var rows = await db.Mods.Where(m => m.Sha256 == sha).ToListAsync();
            await Assert.That(rows).Count().IsEqualTo(2);
            await Assert.That(rows.Select(r => r.ServerId).Order().ToList())
                .IsEquivalentTo(new[] { HopperApi.ServerAId, HopperApi.ServerBId }.Order().ToList());

            var stored = Path.Combine(BlobRoot, sha[..2], sha[2..4], sha);
            await Assert.That(File.Exists(stored)).IsTrue();
        }

        [Test]
        public async Task DeletingASharedJarFromOneServer_LeavesTheOthersDownloadWorking()
        {
            var fileName = "isolation-refcount-" + Guid.NewGuid().ToString("N")[..8] + ".jar";
            var bytes = JarFor(fileName);
            var sha = ShaOf(bytes);

            await SeedAsync(HopperApi.ServerAId, fileName, bytes);
            await SeedAsync(HopperApi.ServerBId, fileName, bytes);

            await using (var scope = HopperApi.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
                var onA = await db.Mods.SingleAsync(m => m.ServerId == HopperApi.ServerAId && m.Sha256 == sha);

                await new DeleteModCommandHandler(db, scope.ServiceProvider.GetRequiredService<IBlobStorage>())
                    .Handle(new DeleteModCommand(HopperApi.ServerAId, onA.Id), CancellationToken.None);
            }

            using var b = HopperApi.AsGameClientB();
            var response = await b.GetAsync($"/api/blobs/{sha}");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(await response.Content.ReadAsByteArrayAsync()).IsEquivalentTo(bytes);
        }

        [Test]
        public async Task DeletingASharedJarFromEveryServer_TakesTheFileWithIt()
        {
            var fileName = "isolation-lastref-" + Guid.NewGuid().ToString("N")[..8] + ".jar";
            var bytes = JarFor(fileName);
            var sha = ShaOf(bytes);

            await SeedAsync(HopperApi.ServerAId, fileName, bytes);
            await SeedAsync(HopperApi.ServerBId, fileName, bytes);

            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
            var blobs = scope.ServiceProvider.GetRequiredService<IBlobStorage>();
            var handler = new DeleteModCommandHandler(db, blobs);

            foreach (var serverId in new[] { HopperApi.ServerAId, HopperApi.ServerBId })
            {
                var row = await db.Mods.SingleAsync(m => m.ServerId == serverId && m.Sha256 == sha);
                await handler.Handle(new DeleteModCommand(serverId, row.Id), CancellationToken.None);
            }

            await Assert.That(blobs.Exists(sha)).IsFalse();
        }

        [Test]
        public async Task DeletingManyAtOnce_KeepsABlobAnotherServerStillCarries()
        {
            // The rule the bulk path must not break: the orphan check is global on purpose, so
            // clearing a whole selection from one server cannot take another server's file.
            var shared = "isolation-bulkshared-" + Guid.NewGuid().ToString("N")[..8] + ".jar";
            var onlyOnA = "isolation-bulkonly-" + Guid.NewGuid().ToString("N")[..8] + ".jar";

            var sharedBytes = JarFor(shared);
            var onlyBytes = JarFor(onlyOnA);
            var sharedSha = ShaOf(sharedBytes);
            var onlySha = ShaOf(onlyBytes);

            await SeedAsync(HopperApi.ServerAId, shared, sharedBytes);
            await SeedAsync(HopperApi.ServerBId, shared, sharedBytes);
            await SeedAsync(HopperApi.ServerAId, onlyOnA, onlyBytes);

            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
            var blobs = scope.ServiceProvider.GetRequiredService<IBlobStorage>();

            var ids = await db.Mods
                .Where(m => m.ServerId == HopperApi.ServerAId && (m.Sha256 == sharedSha || m.Sha256 == onlySha))
                .Select(m => m.Id)
                .ToListAsync();

            var deleted = await new DeleteModsCommandHandler(db, blobs)
                .Handle(new DeleteModsCommand(HopperApi.ServerAId, ids), CancellationToken.None);

            await Assert.That(deleted).IsEqualTo(2);
            await Assert.That(blobs.Exists(sharedSha)).IsTrue();
            await Assert.That(blobs.Exists(onlySha)).IsFalse();
            await Assert.That(await db.Mods.AnyAsync(m => m.ServerId == HopperApi.ServerBId && m.Sha256 == sharedSha)).IsTrue();
        }

        [Test]
        public async Task DeletingManyAtOnce_IgnoresIdsFromAnotherServer()
        {
            var mine = "isolation-bulkmine-" + Guid.NewGuid().ToString("N")[..8] + ".jar";
            var theirs = "isolation-bulktheirs-" + Guid.NewGuid().ToString("N")[..8] + ".jar";

            await SeedAsync(HopperApi.ServerAId, mine, JarFor(mine));
            await SeedAsync(HopperApi.ServerBId, theirs, JarFor(theirs));

            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
            var blobs = scope.ServiceProvider.GetRequiredService<IBlobStorage>();

            var mineId = await db.Mods.Where(m => m.ServerId == HopperApi.ServerAId && m.FileName == mine).Select(m => m.Id).SingleAsync();
            var theirsId = await db.Mods.Where(m => m.ServerId == HopperApi.ServerBId && m.FileName == theirs).Select(m => m.Id).SingleAsync();

            var deleted = await new DeleteModsCommandHandler(db, blobs)
                .Handle(new DeleteModsCommand(HopperApi.ServerAId, new[] { mineId, theirsId }), CancellationToken.None);

            await Assert.That(deleted).IsEqualTo(1);
            await Assert.That(await db.Mods.AnyAsync(m => m.Id == theirsId)).IsTrue();
        }

        // Its own server and its own token: the counter is written after the response completes and
        // the suite runs in parallel, so anything shared would be raced by another test's download.
        private static async Task<(Guid Id, string Token)> AServerOfItsOwnAsync()
        {
            var token = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();

            var server = new Server
            {
                Name = "Served " + token[..8],
                Slug = "served-" + token[..8],
                Token = token,
            };

            db.Servers.Add(server);
            await db.SaveChangesAsync();

            return (server.Id, token);
        }

        private static async Task<long> ServedAsync(Guid serverId, long atLeast)
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                await using var scope = HopperApi.Services.CreateAsyncScope();
                var served = await scope.ServiceProvider.GetRequiredService<HopperDbContext>()
                    .Servers.AsNoTracking().Where(s => s.Id == serverId)
                    .Select(s => s.BytesServed).SingleAsync();

                if (served >= atLeast) return served;

                await Task.Delay(100);
            }

            return -1;
        }

        [Test]
        public async Task AClientThatAlreadyHasTheJar_IsToldSoRatherThanSentItAgain()
        {
            var (serverId, token) = await AServerOfItsOwnAsync();

            var fileName = "isolation-etag-" + Guid.NewGuid().ToString("N")[..8] + ".jar";
            var bytes = JarFor(fileName);
            var sha = ShaOf(bytes);

            await SeedAsync(serverId, fileName, bytes);

            using var client = HopperApi.WithBearer(token);

            var first = await client.GetAsync($"/api/blobs/{sha}");
            await Assert.That(first.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(first.Headers.ETag?.Tag).IsEqualTo($"\"{sha}\"");

            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/blobs/{sha}");
            request.Headers.TryAddWithoutValidation("If-None-Match", $"\"{sha}\"");

            var second = await client.SendAsync(request);

            await Assert.That(second.StatusCode).IsEqualTo(HttpStatusCode.NotModified);
            await Assert.That((await second.Content.ReadAsByteArrayAsync()).Length).IsEqualTo(0);
        }

        [Test]
        public async Task AnInterruptedDownload_CanAskForTheRest()
        {
            var (serverId, token) = await AServerOfItsOwnAsync();

            var fileName = "isolation-range-" + Guid.NewGuid().ToString("N")[..8] + ".jar";
            var bytes = JarFor(fileName);
            var sha = ShaOf(bytes);

            await SeedAsync(serverId, fileName, bytes);

            using var client = HopperApi.WithBearer(token);

            var request = new HttpRequestMessage(HttpMethod.Get, $"/api/blobs/{sha}");
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(2, null);

            var response = await client.SendAsync(request);
            var rest = await response.Content.ReadAsByteArrayAsync();

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.PartialContent);
            await Assert.That(rest).IsEquivalentTo(bytes[2..]);
        }

        [Test]
        public async Task DownloadingABlob_BillsWhatWentOut()
        {
            var (serverId, token) = await AServerOfItsOwnAsync();

            var fileName = "isolation-served-" + Guid.NewGuid().ToString("N")[..8] + ".jar";
            var bytes = JarFor(fileName);
            var sha = ShaOf(bytes);

            await SeedAsync(serverId, fileName, bytes);

            using var client = HopperApi.WithBearer(token);
            var response = await client.GetAsync($"/api/blobs/{sha}");
            var served = (await response.Content.ReadAsByteArrayAsync()).Length;

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.OK);
            await Assert.That(served).IsEqualTo(bytes.Length);

            await Assert.That(await ServedAsync(serverId, served)).IsEqualTo((long)served);
        }

        [Test]
        public async Task ARefusedDownload_BillsNothing()
        {
            // A 404 writes a problem document, not a jar. The server it was aimed at owes nothing.
            var (serverId, token) = await AServerOfItsOwnAsync();

            using var client = HopperApi.WithBearer(token);
            var response = await client.GetAsync($"/api/blobs/{new string('c', 64)}");

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NotFound);

            await Task.Delay(500);

            await using var scope = HopperApi.Services.CreateAsyncScope();
            var served = await scope.ServiceProvider.GetRequiredService<HopperDbContext>()
                .Servers.AsNoTracking().Where(s => s.Id == serverId)
                .Select(s => s.BytesServed).SingleAsync();

            await Assert.That(served).IsEqualTo(0L);
        }

        [Test]
        public async Task DeletingAModIdThatBelongsToAnotherServer_IsANoOp()
        {
            var fileName = "isolation-crossdelete-" + Guid.NewGuid().ToString("N")[..8] + ".jar";
            await SeedAsync(HopperApi.ServerBId, fileName, JarFor(fileName));

            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
            var onB = await db.Mods.SingleAsync(m => m.ServerId == HopperApi.ServerBId && m.FileName == fileName);

            await new DeleteModCommandHandler(db, scope.ServiceProvider.GetRequiredService<IBlobStorage>())
                .Handle(new DeleteModCommand(HopperApi.ServerAId, onB.Id), CancellationToken.None);

            await Assert.That(await db.Mods.AnyAsync(m => m.Id == onB.Id)).IsTrue();
        }

        [Test]
        public async Task SameFileNameOnTwoServers_IsAllowed()
        {
            var fileName = "isolation-samename-" + Guid.NewGuid().ToString("N")[..8] + ".jar";

            await SeedAsync(HopperApi.ServerAId, fileName, JarFor("a-variant"));
            await SeedAsync(HopperApi.ServerBId, fileName, JarFor("b-variant"));

            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();

            var rows = await db.Mods.Where(m => m.FileName == fileName).ToListAsync();

            await Assert.That(rows).Count().IsEqualTo(2);

            await Assert.That(rows.Select(r => r.Sha256).Distinct().Count()).IsEqualTo(2);
        }

        [Test]
        public async Task ClientReport_LandsOnTheServerItsTokenResolvesTo()
        {
            var clientId = "isolation-report-" + Guid.NewGuid().ToString("N");
            using var b = HopperApi.AsGameClientB();

            var response = await b.PostAsync("/api/clients/report", new StringContent(
                $$"""{"clientId":"{{clientId}}","username":null,"mods":[]}""",
                Encoding.UTF8, "application/json"));

            await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.NoContent);

            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
            var stored = await db.Clients.SingleAsync(c => c.ClientId == clientId);

            await Assert.That(stored.ServerId).IsEqualTo(HopperApi.ServerBId);
        }
    }
}
