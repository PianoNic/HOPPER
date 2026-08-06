using HOPPER.Application.Dtos.Modrinth;
using HOPPER.Application.Dtos.Mods;
using HOPPER.Application.Mappings.Mods;
using HOPPER.Application.ModMetadata;
using HOPPER.Application.Modrinth;
using HOPPER.Domain;
using HOPPER.Domain.Enums;
using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Interfaces;
using Mediator;
using Microsoft.EntityFrameworkCore;

namespace HOPPER.Application.Command.Modrinth
{
    /// <summary>One row of an install request. Replace is ticked per row in the plan dialog and
    /// defaults to false, because replacing an installed version is a deliberate act.</summary>
    public sealed record ModrinthInstallItem(string VersionId, bool Replace);

    /// <summary>The commit half of the two-phase add.
    ///
    /// It installs EXACTLY the version ids it was handed and resolves nothing further. That is the
    /// whole point of splitting plan from install: the set the admin saw named in the dialog is the
    /// set that is written, and there is no path on which a dependency appears here that was not on
    /// the screen. Its only re-resolution is the incompatibility re-check below, which is a safety net
    /// against a stale dialog rather than a second walk of the graph.</summary>
    public record InstallModrinthModsCommand(Guid ServerId, IReadOnlyList<ModrinthInstallItem> Items)
        : ICommand<ModrinthInstallResultDto>;

    public class InstallModrinthModsCommandHandler(
        HopperDbContext db,
        IBlobStorage blobs,
        IModrinthClient modrinth,
        ICurrentUserService currentUser) : ICommandHandler<InstallModrinthModsCommand, ModrinthInstallResultDto>
    {
        /// <summary>The resolver caps a plan at 100 nodes, so a request larger than that did not come
        /// from a plan this API produced.</summary>
        private const int MaxItems = 100;

        public async ValueTask<ModrinthInstallResultDto> Handle(
            InstallModrinthModsCommand command, CancellationToken cancellationToken)
        {
            var server = await db.Servers.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == command.ServerId, cancellationToken)
                ?? throw new ServerNotFoundException(command.ServerId);

            var items = command.Items
                .Where(i => !string.IsNullOrWhiteSpace(i.VersionId))
                .GroupBy(i => i.VersionId.Trim(), StringComparer.Ordinal)
                // Same version twice in one request: the tick wins, so a row the admin marked for
                // replacement is not quietly downgraded to a skip by a duplicate entry.
                .Select(g => new ModrinthInstallItem(g.Key, g.Any(i => i.Replace)))
                .ToList();

            if (items.Count == 0)
                throw new ArgumentException("No versions were selected.");

            if (items.Count > MaxItems)
                throw new ArgumentException($"At most {MaxItems} mods can be installed in one request.");

            var installed = new List<ModDto>();
            var adopted = new List<ModrinthAdoptedDto>();
            var replaced = new List<ModDto>();
            var skipped = new List<ModrinthSkippedDto>();
            var failed = new List<ModrinthFailedDto>();

            // One call for the whole batch rather than one per mod. Unknown ids are dropped silently
            // by the bulk endpoint rather than 404ing, so the join is on id and what is missing is
            // whatever did not come back.
            var versions = await modrinth.GetVersionsAsync(items.Select(i => i.VersionId).ToList(), cancellationToken);
            var byId = versions.ToDictionary(v => v.Id, StringComparer.Ordinal);

            foreach (var item in items.Where(i => !byId.ContainsKey(i.VersionId)))
            {
                failed.Add(new ModrinthFailedDto
                {
                    Name = item.VersionId,
                    Error = "Modrinth no longer has this version. Refresh the browser and pick it again.",
                });
            }

            await RefuseIfIncompatibleAsync(command.ServerId, versions, cancellationToken);

            // Titles for the provenance's ProjectName, in one call for the whole batch.
            var projects = await ProjectTitlesAsync(versions, cancellationToken);

            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!byId.TryGetValue(item.VersionId, out var version))
                    continue;

                try
                {
                    await InstallOneAsync(
                        server, version, item.Replace, projects, installed, adopted, replaced, skipped, cancellationToken);
                }
                catch (Exception ex) when (ex is ArgumentException or DuplicateModFileNameException or ModrinthApiException or HttpRequestException or IOException)
                {
                    // Per-item failure does not abort the batch, the same rule the upload path
                    // follows: a batch where one jar's hash did not match is a partial success, and
                    // discarding the nineteen that worked would be the worse outcome.
                    failed.Add(new ModrinthFailedDto
                    {
                        Name = version.PrimaryFile()?.FileName ?? version.Name ?? version.Id,
                        Error = ex.Message,
                    });
                }
            }

            return new ModrinthInstallResultDto
            {
                Installed = installed,
                Adopted = adopted,
                Replaced = replaced,
                Skipped = skipped,
                Failed = failed,
            };
        }

        private async Task InstallOneAsync(
            Server server,
            ModrinthVersion version,
            bool replace,
            IReadOnlyDictionary<string, string> projectTitles,
            List<ModDto> installed,
            List<ModrinthAdoptedDto> adopted,
            List<ModDto> replaced,
            List<ModrinthSkippedDto> skipped,
            CancellationToken cancellationToken)
        {
            var file = version.PrimaryFile()
                ?? throw new ArgumentException($"{version.Name ?? version.Id} publishes no downloadable jar.");

            if (string.IsNullOrWhiteSpace(file.Url) || string.IsNullOrWhiteSpace(file.FileName))
                throw new ArgumentException($"{version.Name ?? version.Id} publishes no downloadable jar.");

            var fileName = ModFileNameValidator.Validate(file.FileName);

            if (string.IsNullOrWhiteSpace(file.Sha512) || string.IsNullOrWhiteSpace(file.Sha1))
            {
                throw new ArgumentException(
                    $"Modrinth published no sha1/sha512 for {fileName}, so the download cannot be verified.");
            }

            // Re-read the current rows rather than trusting the plan: the dialog may be minutes old,
            // and another admin may have added the same mod in the meantime.
            var current = await db.Mods
                .Where(m => m.ServerId == server.Id)
                .ToListAsync(cancellationToken);

            var sameProject = current.FirstOrDefault(
                m => m.ProjectId is not null && string.Equals(m.ProjectId, version.ProjectId, StringComparison.Ordinal));

            if (sameProject is not null && string.Equals(sameProject.VersionId, version.Id, StringComparison.Ordinal))
            {
                skipped.Add(new ModrinthSkippedDto { Name = fileName, Reason = "already on this server at this version." });
                return;
            }

            var nameClash = current.FirstOrDefault(
                m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase));

            var displaced = sameProject ?? nameClash;
            if (displaced is not null && !replace)
            {
                skipped.Add(new ModrinthSkippedDto
                {
                    Name = fileName,
                    Reason = sameProject is not null
                        ? $"{sameProject.FileName} is already installed for this project. Tick Replace to upgrade it."
                        : $"a file named {nameClash!.FileName} is already on this server. Tick Replace to overwrite it.",
                });
                return;
            }

            var (sha256, size, sha1, sha512) = await DownloadAsync(new Uri(file.Url), cancellationToken);

            // Hash check before anything is written to the database. Size is not separately checked:
            // two matching cryptographic digests make a size mismatch impossible.
            if (!string.Equals(sha512, file.Sha512, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(sha1, file.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                await DiscardAsync(sha256, cancellationToken);
                throw new ArgumentException($"Downloaded {fileName} does not match the hashes Modrinth published.");
            }

            var title = projectTitles.GetValueOrDefault(version.ProjectId);

            // Same bytes already here under another name. Modrinth never publishes sha256, so the plan
            // could not possibly have known - this is only ever detectable after the download.
            var sameBytes = current.FirstOrDefault(m => string.Equals(m.Sha256, sha256, StringComparison.Ordinal));
            if (sameBytes is not null && sameBytes != displaced)
            {
                if (sameBytes.HasModrinthProvenance())
                {
                    skipped.Add(new ModrinthSkippedDto
                    {
                        Name = fileName,
                        Reason = $"the same jar is already on this server as {sameBytes.FileName}.",
                    });
                    return;
                }

                // Adopt rather than insert. A second row would make the client write the identical jar
                // twice under two names, which Forge may refuse outright - and the admin gains a mod
                // that now exports with a real CDN URL instead of as an override. The existing
                // filename is kept: it is what the clients already hold.
                //
                // ??=, so a row that predates mod-id extraction gets backfilled here while one that
                // was already read is left exactly as it is. Not folded into ApplyProvenance, which
                // is static and has no blob store.
                sameBytes.ModIds ??= ModIdReader.FromBlob(blobs, sameBytes.Sha256);
                ApplyProvenance(sameBytes, version, file, title, sha1, sha512);
                await db.SaveChangesAsync(cancellationToken);

                adopted.Add(new ModrinthAdoptedDto
                {
                    Mod = sameBytes.ToDto(),
                    Message = $"{sameBytes.FileName} is already this exact jar; it is now tracked as {title ?? version.ProjectId} {version.VersionNumber ?? version.Id}.",
                });
                return;
            }

            var entry = new Mod
            {
                ServerId = server.Id,
                FileName = fileName,
                Sha256 = sha256,
                Size = size,
                UploadedBy = currentUser.Name,

                // Modrinth's body is a network stream that cannot be re-read, so the ids come out of
                // the blob that was just written. See ModIdReader.FromBlob.
                ModIds = ModIdReader.FromBlob(blobs, sha256),
            };

            ApplyProvenance(entry, version, file, title, sha1, sha512);

            string? orphanCandidate = null;
            if (displaced is not null)
            {
                orphanCandidate = displaced.Sha256;
                db.Mods.Remove(displaced);
                replaced.Add(displaced.ToDto());
            }

            db.Mods.Add(entry);

            // One save per mod, matching the importer: a batch that dies halfway leaves what it got
            // through actually installed rather than rolling the whole drop back.
            //
            // The removal and the insert go in the SAME save on purpose. A replacement where the new
            // jar happens to carry the old one's filename would violate the unique (ServerId,
            // FileName) index if the insert ran first; EF Core orders commands that collide on a
            // unique index so the delete precedes the insert, which two separate saves would give up
            // in exchange for a window where the server has no copy of that mod at all.
            await db.SaveChangesAsync(cancellationToken);

            if (orphanCandidate is not null)
                await DiscardAsync(orphanCandidate, cancellationToken);

            installed.Add(entry.ToDto());
        }

        private static void ApplyProvenance(
            Mod mod, ModrinthVersion version, ModrinthVersionFile file, string? title, string sha1, string sha512)
        {
            mod.Source = ModSource.Modrinth;
            mod.ProjectId = version.ProjectId;
            mod.VersionId = version.Id;
            mod.ProjectName = title;
            mod.DownloadUrl = file.Url;

            // Stored lowercase, as the whole codebase represents hex, and taken from what we computed
            // rather than from what upstream sent - they are equal by the check above, and ours is the
            // one that provably describes the bytes on disk.
            mod.Sha1 = sha1;
            mod.Sha512 = sha512;
        }

        /// <summary>Streams the jar into the blob store while hashing it a second and third time. One
        /// pass over the bytes yields sha256 from the store plus sha1 and sha512 from the wrapper.</summary>
        private async Task<(string Sha256, long Size, string Sha1, string Sha512)> DownloadAsync(
            Uri url, CancellationToken cancellationToken)
        {
            // The host allow-list is enforced inside the client, before the socket opens: a version's
            // url is upstream-controlled text and following it anywhere would make HOPPER a request
            // proxy for whoever published it.
            await using var download = await modrinth.OpenDownloadAsync(url, cancellationToken);
            await using var hashing = new HashingStream(download);

            var (sha256, size) = await blobs.SaveAsync(hashing, cancellationToken);
            return (sha256, size, hashing.Sha1Hex, hashing.Sha512Hex);
        }

        /// <summary>Drops a blob nothing references any more. The check is GLOBAL and has no server
        /// filter, exactly as the delete paths do it: narrowing it would delete a file another
        /// server's clients are still being told to download.</summary>
        private async Task DiscardAsync(string sha256, CancellationToken cancellationToken)
        {
            var stillReferenced = await db.Mods.AnyAsync(m => m.Sha256 == sha256, cancellationToken);
            if (!stillReferenced)
                blobs.Delete(sha256);
        }

        /// <summary>The refusal half of the brief. Re-checked here rather than trusted from the plan,
        /// because the dialog may be minutes old and the server's mod set may have moved under it.
        /// Throws before a single byte is downloaded, so an incompatible request writes nothing.</summary>
        private async Task RefuseIfIncompatibleAsync(
            Guid serverId, IReadOnlyList<ModrinthVersion> versions, CancellationToken cancellationToken)
        {
            var declared = versions
                .SelectMany(v => v.Dependencies
                    .Where(d => string.Equals(d.DependencyType?.Trim(), "incompatible", StringComparison.OrdinalIgnoreCase))
                    .Where(d => !string.IsNullOrWhiteSpace(d.ProjectId))
                    .Select(d => (Declaring: v, ProjectId: d.ProjectId!)))
                .ToList();

            if (declared.Count == 0)
                return;

            var onServer = await db.Mods.AsNoTracking()
                .Where(m => m.ServerId == serverId && m.ProjectId != null)
                .Select(m => new { ProjectId = m.ProjectId!, m.ProjectName, m.FileName })
                .ToListAsync(cancellationToken);

            // Project id to the friendliest name available for it, so the 409 names two mods rather
            // than two base62 ids the admin has never seen.
            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in onServer)
                names[row.ProjectId] = row.ProjectName ?? row.FileName;

            var present = onServer.Select(m => m.ProjectId).ToHashSet(StringComparer.Ordinal);

            // A project in this same batch counts as present: installing two mods that declare each
            // other incompatible is the same broken set as installing one against an existing mod.
            foreach (var version in versions)
            {
                present.Add(version.ProjectId);
                names.TryAdd(version.ProjectId, version.PrimaryFile()?.FileName ?? version.Name ?? version.ProjectId);
            }

            var conflict = declared.FirstOrDefault(d => present.Contains(d.ProjectId));
            if (conflict.ProjectId is null)
                return;

            var declaring = conflict.Declaring.PrimaryFile()?.FileName ?? conflict.Declaring.Name ?? conflict.Declaring.Id;
            var other = names.GetValueOrDefault(conflict.ProjectId, conflict.ProjectId);

            throw new IncompatibleModException(
                $"{declaring} declares {other} incompatible, and {other} is on this server. Nothing was installed.");
        }

        private async Task<IReadOnlyDictionary<string, string>> ProjectTitlesAsync(
            IReadOnlyList<ModrinthVersion> versions, CancellationToken cancellationToken)
        {
            var ids = versions
                .Select(v => v.ProjectId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (ids.Count == 0)
                return new Dictionary<string, string>(StringComparer.Ordinal);

            try
            {
                var projects = await modrinth.GetProjectsAsync(ids, cancellationToken);
                return projects
                    .Where(p => !string.IsNullOrWhiteSpace(p.Title))
                    .ToDictionary(p => p.Id, p => p.Title!, StringComparer.Ordinal);
            }
            catch (ModrinthApiException)
            {
                // A cached display name is not worth failing an install over. ProjectName is nullable
                // precisely so this can degrade rather than throw.
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }
    }
}
