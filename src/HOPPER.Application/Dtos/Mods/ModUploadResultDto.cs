namespace HOPPER.Application.Dtos.Mods
{
    public record ModUploadResultDto
    {
        public required IReadOnlyList<ModDto> Uploaded { get; init; }

        public required IReadOnlyList<FailedUploadDto> Failed { get; init; }
    }
}
