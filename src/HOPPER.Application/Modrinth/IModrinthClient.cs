namespace HOPPER.Application.Modrinth
{
    /// <summary>Everything HOPPER asks Modrinth. Narrow on purpose: the dependency resolver is written
    /// against this interface and against nothing else, which is what makes it testable without a
    /// socket.</summary>
    public interface IModrinthClient
    {
        /// <summary>GET /search. limit and offset are clamped on this side, because the API clamps
        /// limit at 100 silently and echoes the clamped value rather than saying so.</summary>
        Task<ModrinthSearchResponse> SearchAsync(
            string? query,
            string? loader,
            string? gameVersion,
            ModrinthSearchIndex index,
            int offset,
            int limit,
            CancellationToken cancellationToken);

        /// <summary>GET /project/{id|slug}. Both a base62 id and a slug resolve on the same path.</summary>
        Task<ModrinthProject> GetProjectAsync(string idOrSlug, CancellationToken cancellationToken);

        /// <summary>GET /projects?ids=[...]. Accepts ids and slugs mixed. Two caveats every caller has
        /// to honour: the response is NOT in the order asked for, so join on id and never by index;
        /// and unknown ids are dropped silently rather than 404ing, so compare counts if it
        /// matters.</summary>
        Task<IReadOnlyList<ModrinthProject>> GetProjectsAsync(IReadOnlyCollection<string> idsOrSlugs, CancellationToken cancellationToken);

        /// <summary>GET /project/{id}/version, newest first. loader and gameVersion are JSON-array
        /// encoded before being URL-encoded - a bare string is not rejected by the API, it is silently
        /// ignored and the whole result comes back unfiltered.</summary>
        Task<IReadOnlyList<ModrinthVersion>> ListVersionsAsync(
            string projectIdOrSlug,
            string? loader,
            string? gameVersion,
            bool includeChangelog,
            CancellationToken cancellationToken);

        Task<ModrinthVersion> GetVersionAsync(string versionId, CancellationToken cancellationToken);

        /// <summary>GET /versions?ids=[...]. Same arbitrary-order and silent-drop caveats as
        /// <see cref="GetProjectsAsync"/>.</summary>
        Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(IReadOnlyCollection<string> versionIds, CancellationToken cancellationToken);

        /// <summary>The two tag lists behind the browser's filter dropdowns. Cached: both are
        /// effectively static, and one of them is 905 entries.</summary>
        Task<ModrinthTags> GetTagsAsync(CancellationToken cancellationToken);

        /// <summary>Opens a version file for reading. Enforces the download host allow-list before the
        /// socket is opened - a version's url is upstream-controlled text, and following it anywhere
        /// would make HOPPER a request proxy for whoever published it.</summary>
        Task<Stream> OpenDownloadAsync(Uri url, CancellationToken cancellationToken);
    }
}
