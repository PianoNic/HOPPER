using HOPPER.Domain.Enums;

namespace HOPPER.Infrastructure.Interfaces
{
    public interface ILocatorJarBuilder
    {
        byte[] Build(Guid serverId, string manifestUrl, string token, ModLoader loader, string? minecraftVersion,
            string? variant = null);
    }

    public sealed class LocatorTemplateMissingException : InvalidOperationException
    {
        public LocatorTemplateMissingException(string path, string reason = "was not found", Exception? inner = null)
            : base($"The locator template jar at '{path}' {reason}. "
                   + "Set Hopper:LocatorTemplateDirectory to the directory holding the jars produced by "
                   + "`cd src/HOPPER.Locator && ./gradlew templates`.", inner)
        {
            Path = path;
        }

        public string Path { get; }
    }
}
