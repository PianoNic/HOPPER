using System.Text;

namespace HOPPER.Application
{
    public static class ServerSlugValidator
    {
        public const int MaxLength = 40;

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
                    builder.Append('-');
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
