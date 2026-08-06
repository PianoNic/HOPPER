namespace HOPPER.Application.Dtos.Mods
{
    /// <summary>Admin view of one distributed jar. Unlike the manifest DTOs these names are not a
    /// fixed contract - the dashboard's TypeScript client is generated from the same OpenAPI
    /// document, so whatever the serializer emits is what the client expects.</summary>
    public record ModDto
    {
        public required Guid Id { get; init; }
        public required string FileName { get; init; }
        public required string Sha256 { get; init; }
        public required long Size { get; init; }
        public string? UploadedBy { get; init; }
        public required DateTime CreatedAt { get; init; }
    }
}
