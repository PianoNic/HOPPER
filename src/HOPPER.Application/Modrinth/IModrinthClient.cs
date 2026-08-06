namespace HOPPER.Application.Modrinth
{
    public interface IModrinthClient
    {
        Task<ModrinthSearchResponse> SearchAsync(
            string? query,
            string? loader,
            string? gameVersion,
            ModrinthSearchIndex index,
            int offset,
            int limit,
            CancellationToken cancellationToken);

        Task<ModrinthProject> GetProjectAsync(string idOrSlug, CancellationToken cancellationToken);

        Task<IReadOnlyList<ModrinthProject>> GetProjectsAsync(IReadOnlyCollection<string> idsOrSlugs, CancellationToken cancellationToken);

        Task<IReadOnlyList<ModrinthVersion>> ListVersionsAsync(
            string projectIdOrSlug,
            string? loader,
            string? gameVersion,
            bool includeChangelog,
            CancellationToken cancellationToken);

        Task<ModrinthVersion> GetVersionAsync(string versionId, CancellationToken cancellationToken);

        Task<IReadOnlyList<ModrinthVersion>> GetVersionsAsync(IReadOnlyCollection<string> versionIds, CancellationToken cancellationToken);

        Task<ModrinthTags> GetTagsAsync(CancellationToken cancellationToken);

        Task<Stream> OpenDownloadAsync(Uri url, CancellationToken cancellationToken);
    }
}
