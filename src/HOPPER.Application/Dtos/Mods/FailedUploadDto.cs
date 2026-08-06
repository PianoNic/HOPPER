namespace HOPPER.Application.Dtos.Mods
{
    /// <summary>One file in a batch that did not become a mod, and why. The reason is the exception's
    /// own message, so the wording an admin sees for a bad filename or a duplicate is the same
    /// whether they uploaded one jar or forty.</summary>
    public record FailedUploadDto
    {
        public required string FileName { get; init; }
        public required string Error { get; init; }
    }
}
