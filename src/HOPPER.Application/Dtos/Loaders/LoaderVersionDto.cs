namespace HOPPER.Application.Dtos.Loaders
{
    public record LoaderVersionDto
    {
        public required string Version { get; init; }

        /// <summary>The build the loader's maintainers point at, which for Forge is not the newest.</summary>
        public required bool Recommended { get; init; }
    }
}
