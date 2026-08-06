using System.Text;

namespace HOPPER.Application
{
    /// <summary>Validates and derives server slugs. A slug is stricter than a display name because
    /// it ends up in a downloaded filename (&lt;slug&gt;-hopper.jar) and in URLs, so it is constrained
    /// at the point of entry rather than escaped at every point of use.</summary>
    public static class ServerSlugValidator
    {
        public const int MaxLength = 40;

        /// <summary>Throws <see cref="ArgumentException"/> unless the value is 1–40 characters of
        /// lowercase alphanumerics and single interior dashes. Uppercase is rejected rather than
        /// folded: a case-insensitive slug would give one server two spellings.</summary>
        public static string Validate(string? slug)
        {
            if (string.IsNullOrWhiteSpace(slug))
                throw new ArgumentException("Slug is required.");

            if (slug.Length > MaxLength)
                throw new ArgumentException($"Slug is longer than {MaxLength} characters: {slug}");

            if (!IsSlug(slug))
                throw new ArgumentException(
                    $"Illegal slug: {slug}. Use lowercase letters, digits and dashes, starting and ending with a letter or digit.");

            return slug;
        }

        /// <summary>Best-effort slug from a display name, for when the admin supplies only a name.
        /// Returns null when nothing usable survives - "  ***  " has no slug, and inventing one
        /// would be worse than asking.</summary>
        public static string? Derive(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var builder = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                if (char.IsAsciiLetterOrDigit(c))
                    builder.Append(char.ToLowerInvariant(c));
                else if (builder.Length > 0 && builder[^1] != '-')
                    builder.Append('-'); // collapse any run of separators into one dash
            }

            var slug = builder.ToString().Trim('-');
            if (slug.Length > MaxLength)
                slug = slug[..MaxLength].TrimEnd('-');

            return slug.Length == 0 ? null : slug;
        }

        private static bool IsSlug(string slug)
        {
            if (!char.IsAsciiLetterLower(slug[0]) && !char.IsAsciiDigit(slug[0]))
                return false;

            if (!char.IsAsciiLetterLower(slug[^1]) && !char.IsAsciiDigit(slug[^1]))
                return false;

            foreach (var c in slug)
            {
                if (c == '-') continue;
                if (char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)) continue;
                return false;
            }

            return true;
        }
    }

    public sealed class ServerNotFoundException(Guid id)
        : InvalidOperationException($"No server with id {id}.")
    {
        public Guid ServerId { get; } = id;
    }

    public sealed class DuplicateServerSlugException(string slug)
        : InvalidOperationException($"A server with the slug {slug} already exists.")
    {
        public string Slug { get; } = slug;
    }
}
