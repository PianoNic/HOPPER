namespace HOPPER.Application
{
    /// <summary>Mirrors Syncer.sanitize() in the Java client. Enforced here at upload time so a jar
    /// the client would refuse to install can never enter the manifest in the first place — a
    /// rejected entry on the client side is a silent partial sync, which is much harder to diagnose
    /// than a 400 at the moment of upload.</summary>
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

            return name;
        }
    }

    public sealed class DuplicateModFileNameException(string fileName)
        : InvalidOperationException($"A mod named {fileName} already exists.")
    {
        public string FileName { get; } = fileName;
    }
}
