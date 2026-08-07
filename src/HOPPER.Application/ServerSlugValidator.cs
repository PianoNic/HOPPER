using System.Text;

namespace HOPPER.Application
{
    public static class ServerSlugValidator
    {
        public const int MaxLength = 40;

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
