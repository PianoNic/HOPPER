namespace HOPPER.Application.Dtos.Jar
{
    public record LocatorJarDto
    {
        public required string FileName { get; init; }

        public required byte[] Content { get; init; }
    }
}
