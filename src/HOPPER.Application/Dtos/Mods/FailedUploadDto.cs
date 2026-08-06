namespace HOPPER.Application.Dtos.Mods
{
    public record FailedUploadDto
    {
        public required string FileName { get; init; }
        public required string Error { get; init; }
    }
}
