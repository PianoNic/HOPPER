namespace HOPPER.Application
{
    public static class ModFileNameValidator
    {
        public static string Validate(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Filename is required.");

            if (name.Contains('/') || name.Contains('\\') || name.Contains("..") || name.StartsWith('.'))
                throw new ArgumentException($"Illegal filename: {name}");

            if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Not a jar: {name}");

            if (name.Length > HopperLimits.MaxFileNameLength)
                throw new ArgumentException($"Filename is too long: {name.Length} characters, the limit is {HopperLimits.MaxFileNameLength}.");

            return name;
        }
    }

    public sealed class DuplicateModFileNameException(string fileName)
        : InvalidOperationException($"A mod named {fileName} already exists.")
    {
        public string FileName { get; } = fileName;
    }
}
