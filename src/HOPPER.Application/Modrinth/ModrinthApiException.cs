using System.Net;

namespace HOPPER.Application.Modrinth
{
    /// <summary>Modrinth answered, and the answer was not usable. Mapped to 502, deliberately: their
    /// API being down, rate-limiting us or returning nonsense is not HOPPER malfunctioning, and the
    /// message names Modrinth so the admin does not file a HOPPER bug for someone else's outage.</summary>
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

    /// <summary>A project, version or slug Modrinth does not have. Its own type because 404 is the one
    /// non-success status that is a normal answer rather than a fault - a stale link in the dashboard
    /// should read as 404, not as "Modrinth is broken".</summary>
    public sealed class ModrinthProjectNotFoundException(string idOrSlug)
        : Exception($"Modrinth has no project or version called {idOrSlug}.")
    {
        public string IdOrSlug { get; } = idOrSlug;
    }
}
