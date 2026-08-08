using HOPPER.Application.Modrinth;

namespace HOPPER.Tests.Modrinth
{
    internal sealed class FakeModrinthClient : IModrinthClient
    {
        public Dictionary<string, ModrinthVersion> Versions { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, ModrinthProject> Projects { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, List<ModrinthVersion>> ProjectVersions { get; } = new(StringComparer.Ordinal);

        public List<string> Calls { get; } = [];

        public Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(
            IReadOnlyCollection<string> versionIds, CancellationToken cancellationToken)
        {
            Calls.Add($"versions:{string.Join(',', versionIds)}");

            var found = versionIds
                .Where(Versions.ContainsKey)
                .Select(id => Versions[id])
                .Reverse()
                .ToList();

            return Task.FromResult<IReadOnlyList<ModrinthVersion>>(found);
        }

        public List<IReadOnlyCollection<string>> ProjectLookups { get; } = [];

        public Task<IReadOnlyList<ModrinthProject>> GetProjectsAsync(
            IReadOnlyCollection<string> idsOrSlugs, CancellationToken cancellationToken)
        {
            Calls.Add($"projects:{string.Join(',', idsOrSlugs)}");
            ProjectLookups.Add(idsOrSlugs.ToList());

            var found = idsOrSlugs
                .Where(Projects.ContainsKey)
                .Select(id => Projects[id])
                .Reverse()
                .ToList();

            return Task.FromResult<IReadOnlyList<ModrinthProject>>(found);
        }

        public Task<IReadOnlyList<ModrinthVersion>> ListVersionsAsync(
            string projectIdOrSlug, string? loader, string? gameVersion, bool includeChangelog, CancellationToken cancellationToken)
        {
            Calls.Add($"list:{projectIdOrSlug}");

            return Task.FromResult<IReadOnlyList<ModrinthVersion>>(
                ProjectVersions.TryGetValue(projectIdOrSlug, out var versions) ? versions : []);
        }

        public Task<ModrinthVersion> GetVersionAsync(string versionId, CancellationToken cancellationToken)
        {
            Calls.Add($"version:{versionId}");

            return Versions.TryGetValue(versionId, out var version)
                ? Task.FromResult(version)
                : throw new ModrinthProjectNotFoundException(versionId);
        }

        public Task<ModrinthProject> GetProjectAsync(string idOrSlug, CancellationToken cancellationToken)
        {
            Calls.Add($"project:{idOrSlug}");

            return Projects.TryGetValue(idOrSlug, out var project)
                ? Task.FromResult(project)
                : throw new ModrinthProjectNotFoundException(idOrSlug);
        }

        public Task<ModrinthSearchResponse> SearchAsync(
            string? query, string? loader, string? gameVersion, ModrinthSearchIndex index,
            int offset, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(new ModrinthSearchResponse());

        public Dictionary<string, ModrinthVersion> ByHash { get; } = new(StringComparer.OrdinalIgnoreCase);

        public List<IReadOnlyCollection<string>> HashLookups { get; } = [];

        public Task<IReadOnlyDictionary<string, ModrinthVersion>> GetVersionsByHashAsync(
            IReadOnlyCollection<string> sha512Hashes, CancellationToken cancellationToken)
        {
            HashLookups.Add(sha512Hashes.ToList());

            IReadOnlyDictionary<string, ModrinthVersion> found = sha512Hashes
                .Where(ByHash.ContainsKey)
                .ToDictionary(h => h, h => ByHash[h], StringComparer.OrdinalIgnoreCase);

            return Task.FromResult(found);
        }

        public Task<ModrinthTags> GetTagsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ModrinthTags([], []));

        public Dictionary<string, byte[]> Downloads { get; } = new(StringComparer.Ordinal);

        public HashSet<string> FailingDownloads { get; } = new(StringComparer.Ordinal);

        public Task<Stream> OpenDownloadAsync(Uri url, CancellationToken cancellationToken)
        {
            Calls.Add($"download:{url}");

            if (FailingDownloads.Contains(url.ToString()))
                throw new ModrinthApiException($"Downloading {url} failed with 503.");

            return Task.FromResult<Stream>(
                new MemoryStream(Downloads.TryGetValue(url.ToString(), out var bytes) ? bytes : []));
        }

        public ModrinthVersion AddMod(
            string projectId,
            string versionId,
            string title,
            string fileName,
            string versionType = "release",
            long size = 1024,
            IReadOnlyList<string>? gameVersions = null,
            IReadOnlyList<string>? loaders = null,
            params ModrinthDependency[] dependencies)
        {
            Projects[projectId] = new ModrinthProject
            {
                Id = projectId,
                Slug = projectId.ToLowerInvariant(),
                Title = title,
            };

            var version = new ModrinthVersion
            {
                Id = versionId,
                ProjectId = projectId,
                Name = $"{title} {versionId}",
                VersionNumber = versionId,
                VersionType = versionType,
                Files =
                [
                    new ModrinthVersionFile
                    {
                        FileName = fileName,
                        Url = $"https://cdn.modrinth.com/data/{projectId}/versions/{versionId}/{fileName}",
                        Primary = true,
                        Size = size,
                        Hashes = new Dictionary<string, string>
                        {
                            ["sha1"] = new string('a', 40),
                            ["sha512"] = new string('b', 128),
                        },
                    },
                ],
                Dependencies = dependencies,
                GameVersions = gameVersions ?? [],
                Loaders = loaders ?? [],
            };

            Versions[versionId] = version;

            if (!ProjectVersions.TryGetValue(projectId, out var list))
                ProjectVersions[projectId] = list = [];

            list.Insert(0, version);

            return version;
        }

        public ModrinthVersion AddDownloadableMod(
            string projectId,
            string versionId,
            string title,
            string fileName,
            byte[] content,
            string? publishedSha1 = null,
            string? publishedSha512 = null,
            params ModrinthDependency[] dependencies)
        {
            var version = AddMod(projectId, versionId, title, fileName, size: content.Length, dependencies: dependencies);
            var file = version.Files[0];

            var fixed_ = version with
            {
                Files =
                [
                    file with
                    {
                        Size = content.Length,
                        Hashes = new Dictionary<string, string>
                        {
                            ["sha1"] = publishedSha1 ?? Convert.ToHexStringLower(System.Security.Cryptography.SHA1.HashData(content)),
                            ["sha512"] = publishedSha512 ?? Convert.ToHexStringLower(System.Security.Cryptography.SHA512.HashData(content)),
                        },
                    },
                ],
            };

            Versions[versionId] = fixed_;
            ProjectVersions[projectId][ProjectVersions[projectId].FindIndex(v => v.Id == versionId)] = fixed_;
            Downloads[file.Url!] = content;

            return fixed_;
        }

        public static ModrinthDependency Required(string projectId) =>
            new() { ProjectId = projectId, DependencyType = "required" };

        public static ModrinthDependency RequiredVersion(string versionId) =>
            new() { VersionId = versionId, DependencyType = "required" };

        public static ModrinthDependency Optional(string projectId) =>
            new() { ProjectId = projectId, DependencyType = "optional" };

        public static ModrinthDependency Incompatible(string projectId) =>
            new() { ProjectId = projectId, DependencyType = "incompatible" };

        public static ModrinthDependency Embedded(string projectId) =>
            new() { ProjectId = projectId, DependencyType = "embedded" };

        public static ModrinthDependency Unnameable(string fileName) =>
            new() { FileName = fileName, DependencyType = "required" };
    }
}
