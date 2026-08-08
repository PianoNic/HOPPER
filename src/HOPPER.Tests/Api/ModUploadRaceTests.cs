using System.Text;
using HOPPER.Application;
using HOPPER.Application.Command.Imports;
using HOPPER.Application.Command.Mods;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HOPPER.Tests.Api
{
    public class ModUploadRaceTests
    {
        private static Stream Jar(string marker) => new MemoryStream(Encoding.UTF8.GetBytes($"PK jar {marker}"));

        private static Stream ClientOnlyJar()
        {
            var buffer = new MemoryStream();

            using (var archive = new System.IO.Compression.ZipArchive(buffer, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
            {
                using var json = archive.CreateEntry("fabric.mod.json").Open();
                json.Write(Encoding.UTF8.GetBytes("{\"id\":\"sodium\",\"environment\":\"client\"}"));
            }

            buffer.Position = 0;
            return buffer;
        }

        private static string Unique(string stem) => $"{stem}-{Guid.NewGuid().ToString("N")[..8]}.jar";

        private static UploadModsCommandHandler Uploads(IServiceProvider services, HopperDbContext db) =>
            new(db,
                services.GetRequiredService<IBlobStorage>(),
                services.GetRequiredService<ICurrentUserService>(),
                services.GetRequiredService<IConfiguration>());

        [Test]
        public async Task Upload_FileNameDifferingOnlyInCase_LandsInFailedAgainstTheRealIndex()
        {
            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
            var handler = Uploads(scope.ServiceProvider, db);

            var lower = Unique("race-case");
            var upper = lower.ToUpperInvariant();

            await handler.Handle(new UploadModsCommand(HopperApi.ServerAId, [new UploadFile(lower, Jar("first"))]), CancellationToken.None);

            var result = await handler.Handle(
                new UploadModsCommand(HopperApi.ServerAId, [new UploadFile(upper, Jar("second"))]), CancellationToken.None);

            await Assert.That(result.Uploaded).IsEmpty();
            await Assert.That(result.Failed.Single().FileName).IsEqualTo(upper);
            await Assert.That(await db.Mods.CountAsync(m => m.ServerId == HopperApi.ServerAId && m.FileName.ToLower() == lower))
                .IsEqualTo(1);
        }

        [Test]
        public async Task Upload_FileNameDifferingOnlyInCaseOnAnotherServer_IsStillAllowed()
        {
            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
            var handler = Uploads(scope.ServiceProvider, db);

            var lower = Unique("race-crossserver");

            await handler.Handle(new UploadModsCommand(HopperApi.ServerAId, [new UploadFile(lower, Jar("a"))]), CancellationToken.None);

            var result = await handler.Handle(
                new UploadModsCommand(HopperApi.ServerBId, [new UploadFile(lower.ToUpperInvariant(), Jar("b"))]), CancellationToken.None);

            await Assert.That(result.Failed).IsEmpty();
            await Assert.That(result.Uploaded).Count().IsEqualTo(1);
        }

        [Test]
        public async Task ResolvePending_FileNameAlreadyOnTheServerInAnotherCase_Is409NotA500()
        {
            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
            var handler = Uploads(scope.ServiceProvider, db);

            var lower = Unique("race-pending");

            await handler.Handle(new UploadModsCommand(HopperApi.ServerAId, [new UploadFile(lower, Jar("already here"))]), CancellationToken.None);

            var import = new ModImport
            {
                ServerId = HopperApi.ServerAId,
                SourceName = "pack.mrpack",
                SourceKind = ImportSourceKind.Upload,
                Status = ImportStatus.Completed,
            };
            db.ModImports.Add(import);

            var pending = new PendingMod
            {
                ServerId = HopperApi.ServerAId,
                ImportId = import.Id,
                Reason = PendingReason.DownloadFailed,
                FileName = lower.ToUpperInvariant(),
            };
            db.PendingMods.Add(pending);
            await db.SaveChangesAsync();

            var resolve = new ResolvePendingModCommandHandler(
                db,
                scope.ServiceProvider.GetRequiredService<IBlobStorage>(),
                scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
                scope.ServiceProvider.GetRequiredService<IConfiguration>());

            await Assert.That(async () => await resolve.Handle(
                    new ResolvePendingModCommand(HopperApi.ServerAId, pending.Id, lower.ToUpperInvariant(), Jar("supplied")),
                    CancellationToken.None))
                .Throws<DuplicateModFileNameException>();
        }

        [Test]
        public async Task ResolvePending_AFreshFileName_StillStoresTheJarAndItsBlob()
        {
            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
            var blobs = scope.ServiceProvider.GetRequiredService<IBlobStorage>();

            var fileName = Unique("race-resolved");

            var import = new ModImport
            {
                ServerId = HopperApi.ServerAId,
                SourceName = "pack.mrpack",
                SourceKind = ImportSourceKind.Upload,
                Status = ImportStatus.Completed,
            };
            db.ModImports.Add(import);

            var pending = new PendingMod
            {
                ServerId = HopperApi.ServerAId,
                ImportId = import.Id,
                Reason = PendingReason.DownloadFailed,
                FileName = fileName,
            };
            db.PendingMods.Add(pending);
            await db.SaveChangesAsync();

            var resolve = new ResolvePendingModCommandHandler(
                db, blobs,
                scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
                scope.ServiceProvider.GetRequiredService<IConfiguration>());

            var stored = await resolve.Handle(
                new ResolvePendingModCommand(HopperApi.ServerAId, pending.Id, fileName, Jar("resolved")),
                CancellationToken.None);

            await Assert.That(stored.FileName).IsEqualTo(fileName);
            await Assert.That(blobs.Exists(stored.Sha256)).IsTrue();
            await Assert.That(await db.PendingMods.AnyAsync(p => p.Id == pending.Id)).IsFalse();
        }

        [Test]
        public async Task ResolvePending_ReadsTheSideOutOfTheJarLikeAnUploadDoes()
        {
            // Collecting a jar by hand is the same act as uploading one, so it should not land on the
            // Both default when the jar says otherwise.
            await using var scope = HopperApi.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HopperDbContext>();
            var blobs = scope.ServiceProvider.GetRequiredService<IBlobStorage>();

            var fileName = Unique("race-sided");

            var import = new ModImport
            {
                ServerId = HopperApi.ServerAId,
                SourceName = "pack.zip",
                SourceKind = ImportSourceKind.Upload,
                Status = ImportStatus.Completed,
            };
            db.ModImports.Add(import);

            var pending = new PendingMod
            {
                ServerId = HopperApi.ServerAId,
                ImportId = import.Id,
                Reason = PendingReason.NoApiKey,
                FileName = fileName,
            };
            db.PendingMods.Add(pending);
            await db.SaveChangesAsync();

            var resolve = new ResolvePendingModCommandHandler(
                db, blobs,
                scope.ServiceProvider.GetRequiredService<ICurrentUserService>(),
                scope.ServiceProvider.GetRequiredService<IConfiguration>());

            var stored = await resolve.Handle(
                new ResolvePendingModCommand(HopperApi.ServerAId, pending.Id, fileName, ClientOnlyJar()),
                CancellationToken.None);

            await Assert.That(stored.Side).IsEqualTo(ModSide.ClientOnly);
        }
    }
}
