using HOPPER.Domain;

namespace HOPPER.Application
{
    public static class ModFileNameValidator
    {
        public static string Validate(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidModFileNameException("Filename is required.");

            if (name.Contains('/') || name.Contains('\\') || name.Contains("..") || name.StartsWith('.'))
                throw new InvalidModFileNameException($"Illegal filename: {name}");

            if (!name.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                throw new InvalidModFileNameException($"Not a jar: {name}");

            if (name.Length > HopperLimits.MaxFileNameLength)
                throw new InvalidModFileNameException($"Filename is too long: {name.Length} characters, the limit is {HopperLimits.MaxFileNameLength}.");

            return name;
        }
    }

    public sealed class InvalidModFileNameException(string message) : RuleViolationException(message);

    public sealed class DuplicateModFileNameException(string fileName)
        : InvalidOperationException($"A mod named {fileName} already exists.")
    {
        public string FileName { get; } = fileName;
    }
}
