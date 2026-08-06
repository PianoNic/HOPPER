using System.Net;

namespace HOPPER.Application.Modrinth
{
    public sealed class ModrinthApiException : Exception
    {
        public ModrinthApiException(string message) : base(message) { }

        public ModrinthApiException(HttpStatusCode status, string? description)
            : base(Describe(status, description))
        {
            Status = status;
        }

        public HttpStatusCode? Status { get; }

        private static string Describe(HttpStatusCode status, string? description) =>
            string.IsNullOrWhiteSpace(description)
                ? $"Modrinth returned {(int)status} {status}."
                : $"Modrinth returned {(int)status}: {description}";
    }

    public sealed class ModrinthProjectNotFoundException(string idOrSlug)
        : Exception($"Modrinth has no project or version called {idOrSlug}.")
    {
        public string IdOrSlug { get; } = idOrSlug;
    }
}
