namespace HOPPER.Application.Dtos.Jar
{
    /// <summary>A finished, patched client jar and the name it should be saved under.</summary>
    public record LocatorJarDto
    {
        /// <summary>&lt;slug&gt;-hopper.jar. Recognisable on disk when a player has jars for two
        /// servers in two instances, which is the whole reason the slug is constrained.</summary>
        public required string FileName { get; init; }

        /// <summary>The complete archive. Bytes rather than a stream because the patch has already
        /// succeeded by the time this exists - see ILocatorJarBuilder.</summary>
        public required byte[] Content { get; init; }
    }
}
