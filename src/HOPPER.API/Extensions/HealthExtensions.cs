using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HOPPER.API.Extensions
{
    public static class HealthExtensions
    {
        public const string LivePath = "/health/live";
        public const string ReadyPath = "/health/ready";

        // Readiness only. Liveness deliberately runs no check at all: a probe that restarts the
        // container because Postgres is down turns one outage into a crash loop that cannot serve
        // the dashboard's own "cannot reach the API" screen either.
        private const string Ready = "ready";

        private static IResult NotFound() => Results.NotFound(new { error = "No such health endpoint. HOPPER serves /health/live and /health/ready." });

        public static IServiceCollection AddHopperHealthChecks(this IServiceCollection services)
        {
            services.AddHealthChecks()
                .AddDbContextCheck<HopperDbContext>("database", tags: [Ready])
                .AddCheck<BlobDirectoryHealthCheck>("blobs", tags: [Ready])
                .AddCheck<LocatorTemplateHealthCheck>("locator-templates", tags: [Ready]);

            return services;
        }

        public static WebApplication MapHopperHealthChecks(this WebApplication app)
        {
            // Before MapFallbackToFile, which answers 200 with index.html for any unmatched path
            // and would otherwise report a wedged instance as healthy from every probe.
            app.MapHealthChecks(LivePath, new() { Predicate = _ => false }).AllowAnonymous();
            app.MapHealthChecks(ReadyPath, new() { Predicate = c => c.Tags.Contains(Ready) }).AllowAnonymous();

            // Everything else under /health is a 404 rather than the fallback's cheerful 200 with
            // index.html. A probe pointed at /health or /healthz is a typo, and a typo that reports
            // healthy is worse than no probe at all. Less specific than the two above, so they win.
            app.Map("/health", NotFound).AllowAnonymous();
            app.Map("/health/{**rest}", NotFound).AllowAnonymous();

            return app;
        }
    }

    // Writable, not merely present: the volume can be mounted read-only, or full, and either way
    // every upload fails while the container looks fine.
    public sealed class BlobDirectoryHealthCheck(IConfiguration configuration) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var root = BlobPaths.Root(configuration);

            try
            {
                Directory.CreateDirectory(root);

                var probe = Path.Combine(root, $".health-{Guid.NewGuid():N}");
                File.WriteAllBytes(probe, []);
                File.Delete(probe);

                return Task.FromResult(HealthCheckResult.Healthy($"{root} is writable"));
            }
            catch (Exception ex)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy($"{root} is not writable", ex));
            }
        }
    }

    // The jar endpoint 503s without these, and that is the one thing a new player hits first.
    public sealed class LocatorTemplateHealthCheck(IConfiguration configuration) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var directory = configuration["Hopper:LocatorTemplateDirectory"];

            if (string.IsNullOrWhiteSpace(directory))
                return Task.FromResult(HealthCheckResult.Healthy("not configured, so the built-in location is used"));

            if (!Directory.Exists(directory))
                return Task.FromResult(HealthCheckResult.Unhealthy($"{directory} does not exist, so every client jar download 503s"));

            var jars = Directory.EnumerateFiles(directory, "*.jar").Take(1).Any();

            return Task.FromResult(jars
                ? HealthCheckResult.Healthy($"{directory} holds templates")
                : HealthCheckResult.Unhealthy($"{directory} holds no template jars, so every client jar download 503s"));
        }
    }
}
