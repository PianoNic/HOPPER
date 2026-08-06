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
    public sealed record ModrinthInstallItem(string VersionId, bool Replace);

    public record InstallModrinthModsCommand(Guid ServerId, IReadOnlyList<ModrinthInstallItem> Items)
        : ICommand<ModrinthInstallResultDto>;

    public class InstallModrinthModsCommandHandler(
        HopperDbContext db,
        IBlobStorage blobs,
        IModrinthClient modrinth,
        ICurrentUserService currentUser) : ICommandHandler<InstallModrinthModsCommand, ModrinthInstallResultDto>
    {
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

            if (!string.Equals(sha512, file.Sha512, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(sha1, file.Sha1, StringComparison.OrdinalIgnoreCase))
            {
                await DiscardAsync(sha256, cancellationToken);
                throw new ArgumentException($"Downloaded {fileName} does not match the hashes Modrinth published.");
            }

            var title = projectTitles.GetValueOrDefault(version.ProjectId);

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

            mod.Sha1 = sha1;
            mod.Sha512 = sha512;
        }

        private async Task<(string Sha256, long Size, string Sha1, string Sha512)> DownloadAsync(
            Uri url, CancellationToken cancellationToken)
        {
            await using var download = await modrinth.OpenDownloadAsync(url, cancellationToken);
            await using var hashing = new HashingStream(download);

            var (sha256, size) = await blobs.SaveAsync(hashing, cancellationToken);
            return (sha256, size, hashing.Sha1Hex, hashing.Sha512Hex);
        }

        private async Task DiscardAsync(string sha256, CancellationToken cancellationToken)
        {
            var stillReferenced = await db.Mods.AnyAsync(m => m.Sha256 == sha256, cancellationToken);
            if (!stillReferenced)
                blobs.Delete(sha256);
        }

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

            var names = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var row in onServer)
                names[row.ProjectId] = row.ProjectName ?? row.FileName;

            var present = onServer.Select(m => m.ProjectId).ToHashSet(StringComparer.Ordinal);

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
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
        }
    }
}
