using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HOPPER.Application.Command.Mods;
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
