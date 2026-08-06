namespace HOPPER.Application.Modrinth
{
    public interface IModrinthDependencyResolver
    {
        Task<ResolveResult> ResolveAsync(ResolveRequest request, CancellationToken cancellationToken);
    }

    /// <summary>Turns a handful of picked version ids into the complete list of what would actually be
    /// added, before anything is added.
    ///
    /// It is a pure planner. It never touches the database - what the server already carries arrives
    /// as <see cref="ResolveRequest.Installed"/> - and it never writes anything anywhere. Its only
    /// collaborator is <see cref="IModrinthClient"/>, which is what lets the whole of it be driven
    /// from fixtures in a unit test with no socket in sight.
    ///
    /// The walk is breadth-first and batched: one /versions call for a level's pinned dependencies,
    /// one /projects call for its unpinned ones, then one version list per newly discovered project.
    /// A forty-mod transitive tree costs single-digit-to-low-double-digit requests rather than forty,
    /// which is the difference between browsing comfortably and being rate-limited.
    ///
    /// The visited set is keyed on PROJECT id, not version id. That is what terminates a cycle:
    /// A requires B requires A revisits A, finds it already chosen, and stops.</summary>
    public class ModrinthDependencyResolver(IModrinthClient client) : IModrinthDependencyResolver
    {
        /// <summary>Hard caps. A pathological or hostile dependency graph must not be able to make
        /// HOPPER loop against Modrinth on an admin's behalf.</summary>
        private const int MaxApiCalls = 60;

        private const int MaxNodes = 100;
        private const int MaxDepth = 12;

        /// <summary>Optionals are resolved for display only, so their budget is separate and small.</summary>
        private const int MaxOptionalResolutions = 25;

        /// <summary>RequiredBy is a LIST, not a single name. Two mods on the same level requiring the
        /// same library is the normal case, not an edge case, and keeping only the first parent would
        /// make the dialog's "required by" caption quietly wrong.</summary>
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

                // Unknown ids are dropped silently by the bulk endpoint rather than 404ing, so the
                // only way to notice a dependency that no longer exists is to compare the counts.
                if (fetched.Count < missing.Count)
                    warnings.Add($"{missing.Count - fetched.Count} referenced projects could not be found on Modrinth.");

                return fetched;
            }

            async Task<ModrinthVersion?> BestVersionAsync(string projectId)
            {
                Spend();
                var versions = await client.ListVersionsAsync(projectId, loader, gameVersion, includeChangelog: false, cancellationToken);

                // Newest first, so the first release is the newest release. Falling back to the newest
                // of anything is deliberate - a mod whose only 1.20.1 Forge build is a beta is still
                // the build that exists - and the node carries Prerelease so the dialog can say so.
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
                        // The cycle guard, and the dedupe. Keyed on the project, so the same mod
                        // reached twice by different paths is one node with two parents.
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

                // One call per level for every title, slug and icon this level needs.
                await ProjectsAsync(added.Select(a => a.Node.ProjectId).ToList());
                foreach (var (node, _) in added)
                    Decorate(node);

                // Keyed on the dependency, valued with every mod on this level that asked for it.
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
                                // The jar is already inside the parent. Adding it ships the same
                                // classes twice and Forge may refuse the duplicate outright, so it is
                                // shown and never enqueued.
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
                                    // Pinned. Use that exact version and do not re-resolve it: the
                                    // author named a build, not a project.
                                    Want(pinnedNext, dependency.VersionId, node.DisplayName);
                                }
                                else if (!string.IsNullOrWhiteSpace(dependency.ProjectId))
                                {
                                    if (!chosen.ContainsKey(dependency.ProjectId))
                                        Want(unpinnedNext, dependency.ProjectId, node.DisplayName);
                                }
                                else
                                {
                                    // Both ids null. Not resolvable through the API at all - surfaced
                                    // to the admin rather than swallowed, and it does not fail the plan.
                                    unresolvable.Add(new UnresolvableNote(
                                        dependency.FileName ?? "an unnamed dependency",
                                        "Modrinth does not identify this dependency, so it cannot be added automatically",
                                        node.DisplayName));
                                }

                                break;

                            default:
                                // Modrinth may add a dependency type at any time. Ignoring an unknown
                                // one is the only safe reading: guessing could install something the
                                // admin never saw, which is the one thing this whole flow exists to
                                // prevent.
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
                // Roots first, then by depth, so the dialog reads top-down as "what you picked, then
                // what that drags in".
                Nodes = chosen.Values.OrderBy(n => n.Depth).ThenBy(n => n.DisplayName, StringComparer.OrdinalIgnoreCase).ToList(),
                Optional = optional,
                Embedded = embedded,
                Incompatible = incompatible,
                Unresolvable = unresolvable,
                Warnings = warnings,
                Blocked = incompatible.Any(i => i.Applies),
                ApiCalls = calls,
            };

            // ---- tail passes ---------------------------------------------------------------

            async Task<List<PlanNode>> ResolveOptionalsAsync()
            {
                // Anything that turned out to be required as well is no longer optional.
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
                    // A declared incompatibility only matters when the other mod is actually here -
                    // either already on the server or about to be added by this same plan.
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

        /// <summary>Merges parents into a node without duplicating one that is already there. A mod
        /// reached by two paths is one node with two "required by" captions, not two nodes.</summary>
        private static void AddParents(PlanNode node, IReadOnlyList<string> parents)
        {
            foreach (var parent in parents)
            {
                if (!node.RequiredBy.Contains(parent, StringComparer.Ordinal))
                    node.RequiredBy.Add(parent);
            }
        }

        /// <summary>Where a node stands against the server's current rows. OtherVersionInstalled and
        /// FileNameTaken both default to skip; replacing is an explicit tick in the dialog, because a
        /// silent upgrade is a mod set changing under the admin without being asked.</summary>
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
