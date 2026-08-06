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
    /// <summary>
    /// The tenant boundary, asserted from both sides. A suite with one server cannot tell "scoped
    /// correctly" apart from "not scoped at all", so every check here runs against two.
    ///
    /// The other half of the story is that blobs are deliberately NOT scoped: the same jar on two
    /// servers is the same bytes and is stored once. That makes the delete path the dangerous one -
    /// an orphan check narrowed to one server would delete a file the other server's clients are
    /// still being told to download - so it is pinned here too.
    /// </summary>
    public class ServerIsolationTests
    {
        private static string BlobRoot =>
            HopperApi.Services.GetRequiredService<IConfiguration>()["Blobs:Directory"]!;

        private static byte[] JarFor(string marker) => Encoding.UTF8.GetBytes($"PK pretend jar {marker}");

        private static string ShaOf(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));

        /// <summary>Seeds through the command handler rather than the HTTP endpoint: the admin surface
        /// is OIDC-gated and this suite has no IdP, but the handler writes to the running host's own
        /// database and blob store - which is what the client endpoints under test then read.</summary>
        private static async Task SeedAsync(Guid serverId, string fileName, byte[] bytes)
        {
            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();

            var handler = new UploadModsCommandHandler(
                db,
                scope.ServiceProvider.GetRequiredService<IBlobStorage>(),
                scope.ServiceProvider.GetRequiredService<ICurrentUserService>());

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
            // 404 rather than 403 on purpose: a client has no business learning that some other
            // server's mod set happens to contain that hash.
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
            // This is the whole point of keeping the blob store global: five servers running the same
            // modpack cost one copy of it.
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
            // The orphan check in DeleteModCommand is global across Mod rows. Narrowing it to the
            // server being edited is the single change that would empty another server's mod set,
            // and nothing else in the system would notice until a player's game failed to launch.
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
        public async Task DeletingAModIdThatBelongsToAnotherServer_IsANoOp()
        {
            // The delete matches on server AND id, so a mod id leaked from elsewhere cannot be used to
            // remove a jar from a server the caller is not working on.
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
            // The unique index is (ServerId, FileName), not FileName. Two servers running the same
            // modpack both have to be able to carry jei.jar - they are different manifests.
            var fileName = "isolation-samename-" + Guid.NewGuid().ToString("N")[..8] + ".jar";

            await SeedAsync(HopperApi.ServerAId, fileName, JarFor("a-variant"));
            await SeedAsync(HopperApi.ServerBId, fileName, JarFor("b-variant"));

            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();

            var rows = await db.Mods.Where(m => m.FileName == fileName).ToListAsync();

            await Assert.That(rows).Count().IsEqualTo(2);
            // Different bytes under the same name on two servers: two distinct blobs, no collision.
            await Assert.That(rows.Select(r => r.Sha256).Distinct().Count()).IsEqualTo(2);
        }

        [Test]
        public async Task ClientReport_LandsOnTheServerItsTokenResolvesTo()
        {
            // ServerId comes from the bearer token, never from the body: a client must not be able to
            // name the server it belongs to.
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
