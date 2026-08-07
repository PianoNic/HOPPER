using System.IO.Compression;
using System.Security.Cryptography;
using HOPPER.Application.ModMetadata;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using HOPPER.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HOPPER.Application.Imports
{
    public interface IPackImporter
    {
        Task RunAsync(Guid importId, CancellationToken cancellationToken);
    }

    public class PackImporter(
        HopperDbContext db,
        IBlobStorage blobs,
        IImportStaging staging,
        IHttpClientFactory httpClientFactory,
        ICurseForgeClient curseForge,
        IConfiguration configuration,
        ILogger<PackImporter> logger) : IPackImporter
    {
        public async Task RunAsync(Guid importId, CancellationToken cancellationToken)
        {
            var import = await db.ModImports.FirstOrDefaultAsync(i => i.Id == importId, cancellationToken);
            if (import is null)
                return;

            import.Status = ImportStatus.Running;
            import.StartedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            var errors = new List<string>();

            try
            {
                if (import.SourceKind == ImportSourceKind.Url)
                    await FetchPackAsync(import, cancellationToken);

                var packPath = staging.PackPath(import.Id);
                if (!File.Exists(packPath))
                    throw new PackImportException("The pack is no longer staged. Start the import again.");

                using var archive = OpenArchive(packPath);

                var detection = PackDetector.Detect(archive);
                import.Format = detection.Format;
                await db.SaveChangesAsync(cancellationToken);

                var context = await PlanContextAsync(import, cancellationToken);

                var plan = detection.Format switch
                {
                    PackFormat.Modrinth => ModrinthPlanner.Plan(archive, detection.Prefix, context),
                    PackFormat.CurseForge => await CurseForgePlanner.PlanAsync(archive, detection.Prefix, context, curseForge, cancellationToken),
                    PackFormat.PrismInstance => PrismPlanner.Plan(archive, detection.Prefix, context),
                    PackFormat.JarArchive => JarArchivePlanner.Plan(archive),
                    _ => throw new PackImportException("Not a recognised modpack or jar archive."),
                };

                RefuseOversizedPlan(archive, plan);

                errors.AddRange(plan.Warnings);

                import.SkippedCount += plan.Skipped;

                foreach (var spec in plan.Pending)
                    await AddPendingAsync(import, spec, cancellationToken);

                foreach (var file in plan.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (file.ZipEntry is not null)
                    {
                        var entry = archive.GetEntry(file.ZipEntry);
                        if (entry is null)
                        {
                            Fail(import, errors, file.FileName, "the entry vanished from the archive");
                            continue;
                        }

                        await using var content = entry.Open();
                        await StoreAsync(import, file.FileName, content, file.Side, errors, cancellationToken);
                    }
                    else
                    {
                        await DownloadAndStoreAsync(import, file, errors, cancellationToken);
                    }
                }

                import.Status = ImportStatus.Completed;
            }
            catch (OperationCanceledException)
            {
                import.Status = ImportStatus.Failed;
                errors.Insert(0, "The import was cancelled.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Import {ImportId} failed", importId);
                import.Status = ImportStatus.Failed;
                errors.Insert(0, ex.Message);
            }
            finally
            {
                import.CompletedAt = DateTime.UtcNow;
                if (errors.Count > 0)
                    import.Error = string.Join("\n", errors.Take(50));

                await RecordTerminalStatusAsync(import);

                try
                {
                    staging.Cleanup(import.Id);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Import {ImportId} left staged files behind", import.Id);
                }
            }
        }

        private async Task RecordTerminalStatusAsync(ModImport import)
        {
            try
            {
                db.ChangeTracker.Clear();

                await db.ModImports
                    .Where(i => i.Id == import.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(i => i.Status, import.Status)
                        .SetProperty(i => i.Format, import.Format)
                        .SetProperty(i => i.ImportedCount, import.ImportedCount)
                        .SetProperty(i => i.SkippedCount, import.SkippedCount)
                        .SetProperty(i => i.PendingCount, import.PendingCount)
                        .SetProperty(i => i.FailedCount, import.FailedCount)
                        .SetProperty(i => i.Error, import.Error)
                        .SetProperty(i => i.CompletedAt, import.CompletedAt)
                        .SetProperty(i => i.UpdatedAt, DateTime.UtcNow), CancellationToken.None);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Import {ImportId} could not record its terminal status", import.Id);
            }
        }

        private async Task<PackPlanContext> PlanContextAsync(ModImport import, CancellationToken cancellationToken)
        {
            var server = await db.Servers.AsNoTracking()
                .Where(s => s.Id == import.ServerId)
                .Select(s => new { s.MinecraftVersion, s.Loader })
                .FirstOrDefaultAsync(cancellationToken);

            return new PackPlanContext
            {
                Target = server is null
                    ? PackPlatform.Unknown
                    : new PackPlatform(server.MinecraftVersion, server.Loader),
                MaxMetadataBytes = HopperLimits.MaxPackMetadataBytes(configuration),
            };
        }

        private async Task FetchPackAsync(ModImport import, CancellationToken cancellationToken)
        {
            if (!Uri.TryCreate(import.SourceName, UriKind.Absolute, out var uri)
                || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new PackImportException("A pack URL must be an absolute https:// URL.");
            }

            using var http = httpClientFactory.CreateClient(ImportHttpClients.Packs);
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
                throw new PackImportException($"Downloading the pack failed with HTTP {(int)response.StatusCode}.");

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await staging.StageAsync(import.Id, stream, MaxImportBytes, cancellationToken);
        }

        private void RefuseOversizedPlan(ZipArchive archive, PackPlan plan)
        {
            var budget = MaxImportBytes;
            long declared = 0;

            foreach (var file in plan.Files)
            {
                declared += file.ZipEntry is not null
                    ? archive.GetEntry(file.ZipEntry)?.Length ?? 0
                    : file.Size ?? 0;

                if (declared > budget)
                {
                    throw new PackImportException(
                        $"This pack declares more than {budget} bytes of mods. Raise Hopper:MaxImportBytes to accept it.");
                }
            }
        }

        private static ZipArchive OpenArchive(string path)
        {
            try
            {
                return ZipFile.OpenRead(path);
            }
            catch (InvalidDataException ex)
            {
                throw new PackImportException($"The pack is not a readable zip archive: {ex.Message}");
            }
        }

        private async Task DownloadAndStoreAsync(ModImport import, PlannedFile file, List<string> errors, CancellationToken cancellationToken)
        {
            var allowed = AllowedHosts();
            var candidates = file.Downloads.Where(u => allowed.Contains(u.Host)).ToList();

            if (candidates.Count == 0)
            {
                var host = file.Downloads.FirstOrDefault()?.Host ?? "(none)";
                await AddPendingAsync(import, new PendingSpec
                {
                    Reason = PendingReason.DownloadFailed,
                    FileName = file.FileName,
                    SourceUrl = file.Downloads.FirstOrDefault()?.ToString(),
                    Detail = $"Download host not allowed: {host}. Add it to Hopper:PackDownloadHosts or supply the jar by hand.",
                }, cancellationToken);
                return;
            }

            Directory.CreateDirectory(staging.WorkDirectory(import.Id));
            var tempPath = Path.Combine(staging.WorkDirectory(import.Id), $"{Guid.NewGuid():N}.part");

            string? lastProblem = null;

            foreach (var uri in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var (sha512, sha1) = await DownloadToAsync(uri, tempPath, cancellationToken);

                    var expected = file.Sha512 ?? file.Sha1;
                    var actual = file.Sha512 is not null ? sha512 : sha1;

                    if (expected is not null && !string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                    {
                        TryDelete(tempPath);
                        await AddPendingAsync(import, new PendingSpec
                        {
                            Reason = PendingReason.HashMismatch,
                            FileName = file.FileName,
                            SourceUrl = uri.ToString(),
                            Detail = "The downloaded bytes do not match the hash the pack declared.",
                        }, cancellationToken);
                        return;
                    }

                    await using (var content = File.OpenRead(tempPath))
                        await StoreAsync(import, file.FileName, content, file.Side, errors, cancellationToken);

                    TryDelete(tempPath);
                    return;
                }
                catch (HttpRequestException ex)
                {
                    lastProblem = ex.Message;
                    TryDelete(tempPath);
                }
                catch (ContentTooLargeException ex)
                {
                    lastProblem = ex.Message;
                    TryDelete(tempPath);
                }
                catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    lastProblem = "the download timed out";
                    TryDelete(tempPath);
                }
            }

            await AddPendingAsync(import, new PendingSpec
            {
                Reason = PendingReason.DownloadFailed,
                FileName = file.FileName,
                SourceUrl = candidates[0].ToString(),
                Detail = $"Every mirror failed{(lastProblem is null ? "" : $": {lastProblem}")}.",
            }, cancellationToken);
        }

        private async Task<(string Sha512, string Sha1)> DownloadToAsync(Uri uri, string path, CancellationToken cancellationToken)
        {
            using var http = httpClientFactory.CreateClient(ImportHttpClients.Packs);
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);

            await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var source = new LimitedStream(body, MaxModBytes, "The download");
                var buffer = new byte[81920];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    sha512.AppendData(buffer, 0, read);
                    sha1.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            return (Convert.ToHexStringLower(sha512.GetHashAndReset()), Convert.ToHexStringLower(sha1.GetHashAndReset()));
        }

        private async Task StoreAsync(ModImport import, string fileName, Stream content, ModSide side, List<string> errors, CancellationToken cancellationToken)
        {
            string validated;
            try
            {
                validated = ModFileNameValidator.Validate(fileName);
            }
            catch (ArgumentException ex)
            {
                Fail(import, errors, fileName, ex.Message);
                return;
            }

            var lowered = validated.ToLowerInvariant();

            if (await db.Mods.AnyAsync(m => m.ServerId == import.ServerId && m.FileName.ToLower() == lowered, cancellationToken))
            {
                import.SkippedCount++;
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            StagedBlob staged;
            try
            {
                staged = await blobs.StageAsync(content, MaxModBytes, cancellationToken);
            }
            catch (ContentTooLargeException ex)
            {
                Fail(import, errors, fileName, ex.Message);
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            var duplicate = false;

            try
            {
                var mod = new Mod
                {
                    ServerId = import.ServerId,
                    FileName = validated,
                    Sha256 = staged.Sha256,
                    Size = staged.Size,
                    UploadedBy = import.CreatedBy,

                    // The pack is believed over the jar. Only when the index said nothing does the
                    // jar's own declaration get a say, which is the whole story for a Prism or
                    // CurseForge import - neither format carries a side.
                    Side = side != ModSide.Both ? side : ModSideReader.FromStaged(blobs, staged),

                    ModIds = ModIdReader.FromStaged(blobs, staged),
                    IconSha256 = await ModIconStore.FromStagedJarAsync(blobs, staged, cancellationToken),
                };

                await using var hold = await BlobLock.HoldAsync(db, staged.Sha256, cancellationToken);

                db.Mods.Add(mod);
                import.ImportedCount++;

                try
                {
                    await db.SaveChangesAsync(cancellationToken);
                    blobs.Promote(staged);
                    await hold.CommitAsync(cancellationToken);
                }
                catch (DbUpdateException ex) when (ex.IsUniqueViolation())
                {
                    db.Entry(mod).State = EntityState.Detached;
                    import.ImportedCount--;
                    duplicate = true;
                }
            }
            finally
            {
                blobs.Discard(staged);
            }

            if (duplicate)
            {
                import.SkippedCount++;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        private void Fail(ModImport import, List<string> errors, string fileName, string reason)
        {
            import.FailedCount++;
            errors.Add($"{fileName}: {reason}");
        }

        private async Task AddPendingAsync(ModImport import, PendingSpec spec, CancellationToken cancellationToken)
        {
            db.PendingMods.Add(new PendingMod
            {
                ServerId = import.ServerId,
                ImportId = import.Id,
                Reason = spec.Reason,
                DisplayName = spec.DisplayName,
                FileName = spec.FileName,
                ProjectId = spec.ProjectId,
                FileId = spec.FileId,
                ExpectedSha1 = spec.ExpectedSha1,
                SourceUrl = spec.SourceUrl,
                Detail = spec.Detail,
            });

            import.PendingCount++;
            await db.SaveChangesAsync(cancellationToken);
        }

        private HashSet<string> AllowedHosts() => PackDownloadHosts.Allowed(configuration);

        private long MaxImportBytes => HopperLimits.MaxImportBytes(configuration);

        private long MaxModBytes => HopperLimits.MaxModBytes(configuration);

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }
}
