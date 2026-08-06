using HOPPER.Application.Dtos.Imports;
using HOPPER.Domain;

namespace HOPPER.Application.Mappings.Imports
{
    public static class ImportMappings
    {
        public static ModImportDto ToDto(this ModImport i) => new()
        {
            Id = i.Id,
            SourceName = i.SourceName,
            SourceKind = i.SourceKind,
            Format = i.Format,
            Status = i.Status,
            ImportedCount = i.ImportedCount,
            SkippedCount = i.SkippedCount,
            PendingCount = i.PendingCount,
            FailedCount = i.FailedCount,
            Error = i.Error,
            StartedAt = i.StartedAt,
            CompletedAt = i.CompletedAt,
            CreatedBy = i.CreatedBy,
            CreatedAt = i.CreatedAt,
        };

        public static PendingModDto ToDto(this PendingMod p) => new()
        {
            Id = p.Id,
            ImportId = p.ImportId,
            Reason = p.Reason,
            DisplayName = p.DisplayName,
            FileName = p.FileName,
            ProjectId = p.ProjectId,
            FileId = p.FileId,
            ExpectedSha1 = p.ExpectedSha1,
            SourceUrl = p.SourceUrl,
            Detail = p.Detail,
            CreatedAt = p.CreatedAt,
        };
    }
}
