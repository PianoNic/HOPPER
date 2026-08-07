namespace HOPPER.Application.Dtos.Loaders
{
    public record LoaderVersionDto
    {
        public required string Version { get; init; }

        public required bool Recommended { get; init; }
    }
}
