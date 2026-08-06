using HOPPER.Application.Modrinth;

namespace HOPPER.Tests.Modrinth
{
    /// <summary>Stands in for the live API. Every resolver test drives this instead of a socket -
    /// nothing in this suite may touch api.modrinth.com, both because a test that depends on someone
    /// else's uptime is not a test and because their limit is 300 requests a minute per address and a
    /// CI loop is exactly how that gets spent.
    ///
    /// It reproduces the two upstream behaviours that actually bite: the bulk endpoints return results
    /// in an order the caller did not ask for, and they drop unknown ids silently rather than
    /// answering 404. A fake that returned things in order would let a resolver that joins by index
    /// pass.</summary>
    internal sealed class FakeModrinthClient : IModrinthClient
    {
        public Dictionary<string, ModrinthVersion> Versions { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, ModrinthProject> Projects { get; } = new(StringComparer.Ordinal);

        /// <summary>Project id to its version list, newest first, as the real endpoint orders it.</summary>
        public Dictionary<string, List<ModrinthVersion>> ProjectVersions { get; } = new(StringComparer.Ordinal);

        /// <summary>Every call made, so a test can assert that a pinned dependency was NOT re-resolved
        /// and that a level cost one bulk call rather than one call per mod.</summary>
        public List<string> Calls { get; } = [];

        public Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(
            IReadOnlyCollection<string> versionIds, CancellationToken cancellationToken)
        {
            Calls.Add($"versions:{string.Join(',', versionIds)}");

            // Reversed on purpose: the response order is not the request order.
            var found = versionIds
                .Where(Versions.ContainsKey)
                .Select(id => Versions[id])
                .Reverse()
                .ToList();

            return Task.FromResult<IReadOnlyList<ModrinthVersion>>(found);
        }

        public Task<IReadOnlyList<ModrinthProject>> GetProjectsAsync(
            IReadOnlyCollection<string> idsOrSlugs, CancellationToken cancellationToken)
        {
            Calls.Add($"projects:{string.Join(',', idsOrSlugs)}");

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

        public Task<ModrinthTags> GetTagsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ModrinthTags([], []));

        /// <summary>Bytes served per URL, for the install tests. Empty for the resolver tests, which
        /// never download anything.</summary>
        public Dictionary<string, byte[]> Downloads { get; } = new(StringComparer.Ordinal);

        /// <summary>Set to make the next download fail, so the batch-continues-past-a-failure rule can
        /// be asserted.</summary>
        public HashSet<string> FailingDownloads { get; } = new(StringComparer.Ordinal);

        public Task<Stream> OpenDownloadAsync(Uri url, CancellationToken cancellationToken)
        {
            Calls.Add($"download:{url}");

            if (FailingDownloads.Contains(url.ToString()))
                throw new ModrinthApiException($"Downloading {url} failed with 503.");

            return Task.FromResult<Stream>(
                new MemoryStream(Downloads.TryGetValue(url.ToString(), out var bytes) ? bytes : []));
        }

        // ---- fixture builders --------------------------------------------------------------

        /// <summary>Registers a project and one version of it, wired so both the bulk lookup and the
        /// version listing find it. Everything a PlanNode needs is populated, because a version with
        /// no primary file is a separate case with its own test.</summary>
        public ModrinthVersion AddMod(
            string projectId,
            string versionId,
            string title,
            string fileName,
            string versionType = "release",
            long size = 1024,
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
            };

            Versions[versionId] = version;

            if (!ProjectVersions.TryGetValue(projectId, out var list))
                ProjectVersions[projectId] = list = [];

            // Newest first, matching the real endpoint's order, which is what "the first release is
            // the newest release" relies on.
            list.Insert(0, version);

            return version;
        }

        /// <summary>A mod that can actually be downloaded: the bytes are served and the published
        /// hashes are the real ones for those bytes, unless overridden to test the mismatch path.</summary>
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
