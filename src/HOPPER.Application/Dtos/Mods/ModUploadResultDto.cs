namespace HOPPER.Application.Dtos.Mods
{
    /// <summary>Result of one multi-file upload. A batch is not all-or-nothing: dropping twenty jars
    /// where one is a duplicate should store nineteen and say so, not reject the lot and make the
    /// admin work out which one offended.</summary>
    public record ModUploadResultDto
    {
        public required IReadOnlyList<ModDto> Uploaded { get; init; }

        /// <summary>Empty on a clean batch. The dashboard raises one toast per entry.</summary>
        public required IReadOnlyList<FailedUploadDto> Failed { get; init; }
    }
}
