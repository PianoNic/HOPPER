using System.IO.Compression;
using System.Security.Cryptography;
using HOPPER.Application.ModMetadata;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace HOPPER.Application.Imports
{
    public interface IPackImporter
    {
        Task RunAsync(Guid importId, CancellationToken cancellationToken);
    }

    /// <summary>Runs one import from staged bytes to Mod rows. Queued -> Staged -> Detected -> Planned
    /// -> Verified -> Stored -> Completed, with anything that could not be fetched landing as a
    /// PendingMod rather than failing the run.
    ///
    /// The import is NOT a transaction. Each file is saved as it lands, so a crash halfway leaves a
    /// coherent partial import the admin can simply re-run - the duplicate check makes a re-run cheap
    /// - and so the dashboard's polling shows real progress rather than nothing followed by
    /// everything.</summary>
    public class PackImporter(
        HopperDbContext db,
        IBlobStorage blobs,
        IImportStaging staging,
        IHttpClientFactory httpClientFactory,
        ICurseForgeClient curseForge,
        IConfiguration configuration,
        ILogger<PackImporter> logger) : IPackImporter
    {
        /// <summary>Modrinth's own upload whitelist, which is the right default for the same reason it
        /// is theirs: a pack index is attacker-controlled text, and following an arbitrary URL out of
        /// one turns HOPPER into a request proxy for whoever wrote the pack.</summary>
        private static readonly string[] DefaultDownloadHosts =
        [
            "cdn.modrinth.com",
            "github.com",
            "raw.githubusercontent.com",
            "gitlab.com",
        ];

        private const long DefaultMaxImportBytes = 2L * 1024 * 1024 * 1024;

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

                var plan = detection.Format switch
                {
                    PackFormat.Modrinth => ModrinthPlanner.Plan(archive, detection.Prefix),
                    PackFormat.CurseForge => await CurseForgePlanner.PlanAsync(archive, detection.Prefix, curseForge, cancellationToken),
                    PackFormat.PrismInstance => PrismPlanner.Plan(archive, detection.Prefix),
                    PackFormat.JarArchive => JarArchivePlanner.Plan(archive),
                    _ => throw new PackImportException("Not a recognised modpack or jar archive."),
                };

                import.SkippedCount += plan.Skipped;

                foreach (var spec in plan.Pending)
                    await AddPendingAsync(import, spec, cancellationToken);

                foreach (var file in plan.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (file.ZipEntry is not null)
                    {
                        // Already in an archive the admin handed us: there is no transport to
                        // distrust, so there is nothing to verify. SaveAsync computes the sha256 that
                        // actually addresses the blob.
                        var entry = archive.GetEntry(file.ZipEntry);
                        if (entry is null)
                        {
                            Fail(import, errors, file.FileName, "the entry vanished from the archive");
                            continue;
                        }

                        await using var content = entry.Open();
                        await StoreAsync(import, file.FileName, content, errors, cancellationToken);
                    }
                    else
                    {
                        await DownloadAndStoreAsync(import, file, errors, cancellationToken);
                    }
                }

                // Completed, not Failed, even with pendings: a pack that needs jars supplied by hand is
                // the normal CurseForge outcome, not a broken run.
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

                // CancellationToken.None: the row has to record what happened even when the host is
                // shutting the worker down, or an interrupted import reads as still Running forever.
                await db.SaveChangesAsync(CancellationToken.None);
                staging.Cleanup(import.Id);
            }
        }

        // ---- Step 2: stage a URL source -------------------------------------------------------

        /// <summary>The pack URL itself is not host-restricted: an admin pasting a link is a deliberate
        /// act, exactly as it is in Prism. The mod URLs found INSIDE a pack are restricted, because
        /// those are chosen by whoever wrote the pack rather than by the person clicking.</summary>
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

        // ---- Steps 5-6: download, verify, store ----------------------------------------------

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

            // downloads[] is a mirror list of the same file, so a failure moves to the next one before
            // it becomes the admin's problem.
            foreach (var uri in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var (sha512, sha1) = await DownloadToAsync(uri, tempPath, cancellationToken);

                    // sha512 when the index published one, else sha1. This is an integrity check
                    // against what the pack described, not a security boundary - but a file that is
                    // not what the pack named must never become a Mod row.
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
                        await StoreAsync(import, file.FileName, content, errors, cancellationToken);

                    TryDelete(tempPath);
                    return;
                }
                catch (HttpRequestException ex)
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

        /// <summary>Streams to disk while computing SHA-512 and SHA-1 in the same pass, because the
        /// index may publish either and a second read of a 200 MB jar to hash it is a second read.</summary>
        private async Task<(string Sha512, string Sha1)> DownloadToAsync(Uri uri, string path, CancellationToken cancellationToken)
        {
            using var http = httpClientFactory.CreateClient(ImportHttpClients.Packs);
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            using var sha512 = IncrementalHash.CreateHash(HashAlgorithmName.SHA512);
            using var sha1 = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
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

        private async Task StoreAsync(ModImport import, string fileName, Stream content, List<string> errors, CancellationToken cancellationToken)
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

            // Imports are re-runnable: a jar this server already carries is skipped rather than
            // conflicting, so re-importing a pack after resolving its pendings does not fail on
            // everything that already worked.
            if (await db.Mods.AnyAsync(m => m.ServerId == import.ServerId && m.FileName == validated, cancellationToken))
            {
                import.SkippedCount++;
                await db.SaveChangesAsync(cancellationToken);
                return;
            }

            var (sha256, size) = await blobs.SaveAsync(content, cancellationToken);

            db.Mods.Add(new Mod
            {
                ServerId = import.ServerId,
                FileName = validated,
                Sha256 = sha256,
                Size = size,
                UploadedBy = import.CreatedBy,

                // After the save, by content address: the stream that got here may be an override
                // inside the pack archive, which cannot seek. See ModIdReader.FromBlob.
                ModIds = ModIdReader.FromBlob(blobs, sha256),
            });

            // One save per file. That is what makes the counters the dashboard polls mean something
            // while the import is still running, and what makes a crash halfway leave the jars that
            // did land actually stored.
            import.ImportedCount++;
            await db.SaveChangesAsync(cancellationToken);
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

        private HashSet<string> AllowedHosts()
        {
            var configured = configuration.GetSection("Hopper:PackDownloadHosts").Get<string[]>();
            var hosts = configured is { Length: > 0 } ? configured : DefaultDownloadHosts;
            return hosts.ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private long MaxImportBytes => configuration.GetValue("Hopper:MaxImportBytes", DefaultMaxImportBytes);

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
