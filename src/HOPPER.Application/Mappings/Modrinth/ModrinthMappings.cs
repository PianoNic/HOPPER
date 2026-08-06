using HOPPER.Application.Dtos.Modrinth;
using HOPPER.Application.Modrinth;

namespace HOPPER.Application.Mappings.Modrinth
{
    public static class ModrinthMappings
    {
        public static ModrinthSearchHitDto ToDto(this ModrinthHit hit, bool installed) => new()
        {
            ProjectId = hit.ProjectId,
            Slug = hit.Slug,

            Title = hit.Title ?? hit.Slug ?? hit.ProjectId,
            Description = hit.Description,
            Author = hit.Author,
            IconUrl = hit.IconUrl,
            Downloads = hit.Downloads,
            Follows = hit.Follows,
            Categories = hit.Categories,
            DateModified = hit.DateModified?.UtcDateTime,
            Installed = installed,
        };

        public static ModrinthProjectDto ToDto(this ModrinthProject project) => new()
        {
            Id = project.Id,
            Slug = project.Slug,
            Title = project.Title ?? project.Slug ?? project.Id,
            Description = project.Description,
            Body = project.Body,
            IconUrl = project.IconUrl,
            SourceUrl = project.SourceUrl,
            IssuesUrl = project.IssuesUrl,
            Downloads = project.Downloads,
            Followers = project.Followers,
            Categories = project.Categories,
            Loaders = project.Loaders,
            GameVersions = project.GameVersions,
        };

        public static ModrinthVersionDto ToDto(this ModrinthVersion version, bool installed)
        {
            var file = version.PrimaryFile();

            return new ModrinthVersionDto
            {
                Id = version.Id,
                ProjectId = version.ProjectId,
                Name = version.Name,
                VersionNumber = version.VersionNumber,
                VersionType = version.VersionType,
                DatePublished = version.DatePublished?.UtcDateTime,
                Downloads = version.Downloads,
                GameVersions = version.GameVersions,
                Loaders = version.Loaders,
                FileName = file?.FileName,
                FileSize = file?.Size ?? 0,
                Installed = installed,
            };
        }

        public static ModrinthPlanNodeDto ToDto(this PlanNode node) => new()
        {
            ProjectId = node.ProjectId,
            Slug = node.ProjectSlug,
            Title = node.DisplayName,
            IconUrl = node.IconUrl,
            VersionId = node.VersionId,
            VersionNumber = node.VersionNumber,
            VersionType = node.VersionType,
            FileName = node.FileName,
            FileSize = node.FileSize,
            Kind = node.Kind,
            Status = node.Status,
            Depth = node.Depth,
            RequiredBy = node.RequiredBy,
            Pinned = node.Pinned,
            Prerelease = node.Prerelease,
        };

        public static ModrinthInstallPlanDto ToDto(this ResolveResult result)
        {
            var adding = result.Nodes.Where(n => n.Status == PlanNodeStatus.New).ToList();

            return new ModrinthInstallPlanDto
            {
                Nodes = result.Nodes.Select(n => n.ToDto()).ToList(),
                Optional = result.Optional.Select(n => n.ToDto()).ToList(),
                Embedded = result.Embedded.Select(e => new ModrinthEmbeddedDto
                {
                    ProjectId = e.ProjectId,
                    Title = e.Title,
                    BundledBy = e.BundledBy,
                }).ToList(),
                Incompatible = result.Incompatible.Select(i => new ModrinthIncompatibleDto
                {
                    ProjectId = i.ProjectId,
                    Title = i.Title,
                    DeclaredBy = i.DeclaredBy,
                    Applies = i.Applies,
                }).ToList(),
                Unresolvable = result.Unresolvable.Select(u => new ModrinthUnresolvableDto
                {
                    Name = u.Name,
                    Reason = u.Reason,
                    RequestedBy = u.RequestedBy,
                }).ToList(),
                Warnings = result.Warnings,
                Blocked = result.Blocked,
                AddCount = adding.Count,
                AddSize = adding.Sum(n => n.FileSize),
            };
        }

        public static ModrinthTagsDto ToDto(this ModrinthTags tags) => new()
        {
            Loaders = tags.Loaders
                .Where(l => l.SupportedProjectTypes.Contains("mod", StringComparer.Ordinal))
                .Select(l => l.Name)
                .ToList(),
            GameVersions = tags.GameVersions

                .Where(v => string.Equals(v.VersionType, "release", StringComparison.Ordinal))
                .Select(v => new ModrinthGameVersionDto { Version = v.Version, Major = v.Major })
                .ToList(),
        };
    }
}
