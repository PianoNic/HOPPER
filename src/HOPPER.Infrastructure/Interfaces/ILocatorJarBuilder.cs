using HOPPER.Domain.Enums;

namespace HOPPER.Infrastructure.Interfaces
{
    /// <summary>Produces the per-server client jar.
    ///
    /// A jar is a zip, so this is a zip edit and nothing more: HOPPER ships one template jar per
    /// loader generation, built once from src/HOPPER.Locator/, and every download is a copy of the
    /// right one with one entry written or replaced. No JDK, no Gradle and no process launch at
    /// request time - the toolchain exists in the Docker build stage and never reaches the running
    /// image.</summary>
    public interface ILocatorJarBuilder
    {
        /// <summary>Returns the complete patched jar. A byte[] rather than a Stream on purpose: the
        /// archive is finished in memory before the caller can write a single byte to a response, so
        /// a missing or corrupt template fails as a clean error instead of as a truncated download the
        /// player then has to diagnose in a Forge crash log.
        ///
        /// The loader and Minecraft version pick the template. A loader resolves ONE jar out of mods/,
        /// so handing a Fabric server a Forge jar is not a degraded install - it is a jar the loader
        /// never looks inside.</summary>
        /// <exception cref="LocatorLoaderNotConfiguredException">The server has no loader set.</exception>
        /// <exception cref="LocatorTemplateMissingException">The template is absent, unreadable, or
        /// is not the HOPPER locator jar for that loader.</exception>
        byte[] Build(Guid serverId, string manifestUrl, string token, ModLoader loader, string? minecraftVersion);
    }

    /// <summary>The template jar could not be used. Carries the resolved absolute path because the
    /// only useful thing to tell an admin is where HOPPER looked and which key moves it.</summary>
    public sealed class LocatorTemplateMissingException : InvalidOperationException
    {
        public LocatorTemplateMissingException(string path, string reason = "was not found", Exception? inner = null)
            : base($"The locator template jar at '{path}' {reason}. "
                   + "Set Hopper:LocatorTemplateDirectory to the directory holding the jars produced by "
                   + "`cd src/HOPPER.Locator && ./gradlew build`.", inner)
        {
            Path = path;
        }

        public string Path { get; }
    }
}
