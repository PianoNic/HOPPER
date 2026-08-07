using HOPPER.Application.Dtos.Mods;
using HOPPER.Domain;

namespace HOPPER.Application.Mappings.Mods
{
    public static class ModMappings
    {
        public static ModDto ToDto(this Mod m) => new()
        {
            Id = m.Id,
            FileName = m.FileName,
            Sha256 = m.Sha256,
            Size = m.Size,
            UploadedBy = m.UploadedBy,
            CreatedAt = m.CreatedAt,
            Source = m.Source,
            ProjectId = m.ProjectId,
            VersionId = m.VersionId,
            ProjectName = m.ProjectName,
            DownloadUrl = m.DownloadUrl,
            IconSha256 = m.IconSha256,
            IconUrl = m.IconUrl,
        };
    }
}
