namespace HOPPER.Application.Modrinth
{
    public interface IModrinthDependencyResolver
    {
        Task<ResolveResult> ResolveAsync(ResolveRequest request, CancellationToken cancellationToken);
    }

    public class ModrinthDependencyResolver(IModrinthClient client) : IModrinthDependencyResolver
    {
        private const int MaxApiCalls = 60;

        private const int MaxNodes = 100;
        private const int MaxDepth = 12;

        private const int MaxOptionalResolutions = 25;

        private sealed record Frontier(
            ModrinthVersion Version,
            PlanNodeKind Kind,
            bool Pinned,
            IReadOnlyList<string> RequiredBy);

        public async Task<ResolveResult> ResolveAsync(ResolveRequest request, CancellationToken cancellationToken)
        {
            var loader = ModrinthFacets.ValidateLoader(request.Loader);
            var gameVersion = ModrinthFacets.ValidateGameVersion(request.GameVersion);

            var chosen = new Dictionary<string, PlanNode>(StringComparer.Ordinal);
            var optionalCandidates = new Dictionary<string, string>(StringComparer.Ordinal);
            var incompatibleClaims = new List<(string ProjectId, string DeclaredBy)>();
            var embedded = new List<EmbeddedNote>();
            var unresolvable = new List<UnresolvableNote>();
            var warnings = new List<string>();
            var projects = new Dictionary<string, ModrinthProject>(StringComparer.Ordinal);
            var calls = 0;

            void Spend()
            {
                if (++calls > MaxApiCalls)
                {
                    throw new ResolveBudgetExceededException(
                        "This dependency tree is larger than HOPPER resolves in one go. Add the mods in smaller batches.");
                }
            }

            async Task<IReadOnlyList<ModrinthVersion>> VersionsAsync(IReadOnlyCollection<string> ids)
            {
                Spend();
                return await client.GetVersionsAsync(ids, cancellationToken);
            }

            async Task<IReadOnlyList<ModrinthProject>> ProjectsAsync(IReadOnlyCollection<string> ids)
            {
                var missing = ids.Where(id => !projects.ContainsKey(id)).Distinct(StringComparer.Ordinal).ToList();
                if (missing.Count == 0)
                    return [];

                Spend();
                var fetched = await client.GetProjectsAsync(missing, cancellationToken);
                foreach (var project in fetched)
                    projects[project.Id] = project;

                if (fetched.Count < missing.Count)
                    warnings.Add($"{missing.Count - fetched.Count} referenced projects could not be found on Modrinth.");

                return fetched;
            }

            async Task<ModrinthVersion?> BestVersionAsync(string projectId)
            {
                Spend();
                var versions = await client.ListVersionsAsync(projectId, loader, gameVersion, includeChangelog: false, cancellationToken);

                return versions.FirstOrDefault(v => v.IsRelease()) ?? versions.FirstOrDefault();
            }

            void Decorate(PlanNode node)
            {
                if (!projects.TryGetValue(node.ProjectId, out var project))
                    return;

                node.ProjectTitle = project.Title;
                node.ProjectSlug = project.Slug;
                node.IconUrl = project.IconUrl;
            }

            var rootIds = request.RootVersionIds
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (rootIds.Count == 0)
            {
                return new ResolveResult
                {
                    Nodes = [],
                    Optional = [],
                    Embedded = [],
                    Incompatible = [],
                    Unresolvable = [],
                    Warnings = [],
                    Blocked = false,
                    ApiCalls = 0,
                };
            }

            var rootVersions = await VersionsAsync(rootIds);
            if (rootVersions.Count < rootIds.Count)
                warnings.Add($"{rootIds.Count - rootVersions.Count} of the selected versions are no longer on Modrinth.");

            var frontier = rootVersions
                .Select(v => new Frontier(v, PlanNodeKind.Root, Pinned: false, RequiredBy: []))
                .ToList();

            for (var depth = 0; frontier.Count > 0; depth++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (depth > MaxDepth)
                {
                    warnings.Add("The dependency tree is deeper than HOPPER follows; anything past this point was not resolved.");
                    break;
                }

                var added = new List<(PlanNode Node, ModrinthVersion Version)>();

                foreach (var item in frontier)
                {
                    var projectId = item.Version.ProjectId;
                    if (string.IsNullOrWhiteSpace(projectId))
                        continue;

                    if (chosen.TryGetValue(projectId, out var existing))
                    {
                        if (!string.Equals(existing.VersionId, item.Version.Id, StringComparison.Ordinal))
                        {
                            warnings.Add(
                                $"{existing.DisplayName} was requested at two versions; keeping {existing.VersionNumber ?? existing.VersionId}.");
                        }

                        AddParents(existing, item.RequiredBy);
                        continue;
                    }

                    var file = item.Version.PrimaryFile();
                    if (file is null || string.IsNullOrWhiteSpace(file.Url) || string.IsNullOrWhiteSpace(file.FileName))
                    {
                        unresolvable.Add(new UnresolvableNote(
                            item.Version.Name ?? projectId,
                            "this version publishes no downloadable jar",
                            item.RequiredBy.FirstOrDefault() ?? "your selection"));
                        continue;
                    }

                    var node = new PlanNode
                    {
                        ProjectId = projectId,
                        VersionId = item.Version.Id,
                        VersionNumber = item.Version.VersionNumber,
                        VersionType = item.Version.VersionType,
                        FileName = file.FileName,
                        FileSize = file.Size,
                        DownloadUrl = file.Url,
                        Sha1 = file.Sha1,
                        Sha512 = file.Sha512,
                        Kind = item.Kind,
                        Depth = depth,
                        Pinned = item.Pinned,
                        Prerelease = !item.Version.IsRelease(),
                    };

                    AddParents(node, item.RequiredBy);

                    chosen[projectId] = node;
                    added.Add((node, item.Version));

                    if (chosen.Count > MaxNodes)
                    {
                        throw new ResolveBudgetExceededException(
                            $"This dependency tree is larger than HOPPER resolves in one go (over {MaxNodes} mods). Add the mods in smaller batches.");
                    }
                }

                await ProjectsAsync(added.Select(a => a.Node.ProjectId).ToList());
                foreach (var (node, _) in added)
                    Decorate(node);

                var pinnedNext = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                var unpinnedNext = new Dictionary<string, List<string>>(StringComparer.Ordinal);

                void Want(Dictionary<string, List<string>> into, string key, string parent)
                {
                    if (!into.TryGetValue(key, out var parents))
                        into[key] = parents = [];

                    if (!parents.Contains(parent, StringComparer.Ordinal))
                        parents.Add(parent);
                }

                foreach (var (node, version) in added)
                {
                    foreach (var dependency in version.Dependencies)
                    {
                        switch (dependency.DependencyType?.Trim().ToLowerInvariant())
                        {
                            case "embedded":

                                embedded.Add(new EmbeddedNote(
                                    dependency.ProjectId ?? string.Empty,
                                    dependency.FileName,
                                    node.DisplayName));
                                break;

                            case "incompatible":
                                if (!string.IsNullOrWhiteSpace(dependency.ProjectId))
                                    incompatibleClaims.Add((dependency.ProjectId, node.DisplayName));
                                break;

                            case "optional":
                                if (!string.IsNullOrWhiteSpace(dependency.ProjectId) && !chosen.ContainsKey(dependency.ProjectId))
                                    optionalCandidates.TryAdd(dependency.ProjectId, node.DisplayName);
                                break;

                            case "required":
                                if (!string.IsNullOrWhiteSpace(dependency.VersionId))
                                {
                                    Want(pinnedNext, dependency.VersionId, node.DisplayName);
                                }
                                else if (!string.IsNullOrWhiteSpace(dependency.ProjectId))
                                {
                                    if (!chosen.ContainsKey(dependency.ProjectId))
                                        Want(unpinnedNext, dependency.ProjectId, node.DisplayName);
                                }
                                else
                                {
                                    unresolvable.Add(new UnresolvableNote(
                                        dependency.FileName ?? "an unnamed dependency",
                                        "Modrinth does not identify this dependency, so it cannot be added automatically",
                                        node.DisplayName));
                                }

                                break;

                            default:

                                break;
                        }
                    }
                }

                var next = new List<Frontier>();

                if (pinnedNext.Count > 0)
                {
                    var pinned = await VersionsAsync(pinnedNext.Keys.ToList());
                    if (pinned.Count < pinnedNext.Count)
                        warnings.Add($"{pinnedNext.Count - pinned.Count} pinned dependency versions are no longer on Modrinth.");

                    foreach (var version in pinned)
                    {
                        next.Add(new Frontier(
                            version,
                            PlanNodeKind.Required,
                            Pinned: true,
                            RequiredBy: pinnedNext.GetValueOrDefault(version.Id) ?? []));
                    }
                }

                if (unpinnedNext.Count > 0)
                {
                    var wanted = unpinnedNext.Keys.ToList();
                    await ProjectsAsync(wanted);

                    foreach (var projectId in wanted)
                    {
                        if (!projects.TryGetValue(projectId, out var project))
                            continue;

                        var parents = unpinnedNext.GetValueOrDefault(projectId) ?? [];

                        var best = await BestVersionAsync(project.Id);
                        if (best is null)
                        {
                            unresolvable.Add(new UnresolvableNote(
                                project.Title ?? project.Id,
                                $"no version for {loader} {gameVersion}",
                                parents.FirstOrDefault() ?? "your selection"));
                            continue;
                        }

                        next.Add(new Frontier(
                            best,
                            PlanNodeKind.Required,
                            Pinned: false,
                            RequiredBy: parents));
                    }
                }

                frontier = next;
            }

            var optional = await ResolveOptionalsAsync();
            var incompatible = await ResolveIncompatibilitiesAsync();

            foreach (var node in chosen.Values)
                node.Status = StatusOf(node, request.Installed);

            foreach (var node in optional)
                node.Status = StatusOf(node, request.Installed);

            return new ResolveResult
            {
                Nodes = chosen.Values.OrderBy(n => n.Depth).ThenBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
                Optional = optional,
                Embedded = embedded,
                Incompatible = incompatible,
                Unresolvable = unresolvable,
                Warnings = warnings,
                Blocked = incompatible.Any(i => i.Applies),
                ApiCalls = calls,
            };

            async Task<List<PlanNode>> ResolveOptionalsAsync()
            {
                var wanted = optionalCandidates.Keys.Where(id => !chosen.ContainsKey(id)).ToList();
                if (wanted.Count == 0)
                    return [];

                if (wanted.Count > MaxOptionalResolutions)
                {
                    warnings.Add($"Only the first {MaxOptionalResolutions} optional dependencies were resolved.");
                    wanted = wanted.Take(MaxOptionalResolutions).ToList();
                }

                await ProjectsAsync(wanted);

                var nodes = new List<PlanNode>();
                foreach (var projectId in wanted)
                {
                    if (!projects.TryGetValue(projectId, out var project))
                        continue;

                    var best = await BestVersionAsync(project.Id);
                    var file = best?.PrimaryFile();

                    if (best is null || file is null || string.IsNullOrWhiteSpace(file.Url) || string.IsNullOrWhiteSpace(file.FileName))
                    {
                        unresolvable.Add(new UnresolvableNote(
                            project.Title ?? project.Id,
                            $"optional, and no version exists for {loader} {gameVersion}",
                            optionalCandidates.GetValueOrDefault(projectId) ?? "your selection"));
                        continue;
                    }

                    var node = new PlanNode
                    {
                        ProjectId = project.Id,
                        VersionId = best.Id,
                        VersionNumber = best.VersionNumber,
                        VersionType = best.VersionType,
                        FileName = file.FileName,
                        FileSize = file.Size,
                        DownloadUrl = file.Url,
                        Sha1 = file.Sha1,
                        Sha512 = file.Sha512,
                        Kind = PlanNodeKind.Optional,
                        Depth = 1,
                        Prerelease = !best.IsRelease(),
                    };

                    if (optionalCandidates.TryGetValue(projectId, out var parent))
                        node.RequiredBy.Add(parent);

                    Decorate(node);
                    nodes.Add(node);
                }

                return nodes;
            }

            async Task<List<IncompatibleNote>> ResolveIncompatibilitiesAsync()
            {
                if (incompatibleClaims.Count == 0)
                    return [];

                await ProjectsAsync(incompatibleClaims.Select(c => c.ProjectId).ToList());

                var notes = new List<IncompatibleNote>();
                foreach (var (projectId, declaredBy) in incompatibleClaims.DistinctBy(c => (c.ProjectId, c.DeclaredBy)))
                {
                    var applies = chosen.ContainsKey(projectId)
                        || request.Installed.Any(m => string.Equals(m.ProjectId, projectId, StringComparison.Ordinal));

                    notes.Add(new IncompatibleNote(
                        projectId,
                        projects.TryGetValue(projectId, out var project) ? project.Title : null,
                        declaredBy,
                        applies));
                }

                return notes;
            }
        }

        private static void AddParents(PlanNode node, IReadOnlyList<string> parents)
        {
            foreach (var parent in parents)
            {
                if (!node.RequiredBy.Contains(parent, StringComparer.Ordinal))
                    node.RequiredBy.Add(parent);
            }
        }

        public static PlanNodeStatus StatusOf(PlanNode node, IReadOnlyList<InstalledMod> installed)
        {
            var sameProject = installed.FirstOrDefault(
                m => !string.IsNullOrWhiteSpace(m.ProjectId) && string.Equals(m.ProjectId, node.ProjectId, StringComparison.Ordinal));

            if (sameProject is not null)
            {
                return string.Equals(sameProject.VersionId, node.VersionId, StringComparison.Ordinal)
                    ? PlanNodeStatus.AlreadyInstalled
                    : PlanNodeStatus.OtherVersionInstalled;
            }

            if (installed.Any(m => string.Equals(m.FileName, node.FileName, StringComparison.OrdinalIgnoreCase)))
                return PlanNodeStatus.FileNameTaken;

            return PlanNodeStatus.New;
        }
    }
}
