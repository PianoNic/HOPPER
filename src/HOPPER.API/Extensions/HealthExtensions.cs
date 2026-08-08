using HOPPER.Infrastructure;
using HOPPER.Infrastructure.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HOPPER.API.Extensions
{
    public static class HealthExtensions
    {
        public const string LivePath = "/health/live";
        public const string ReadyPath = "/health/ready";

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
            app.MapHealthChecks(LivePath, new() { Predicate = _ => false }).AllowAnonymous();
            app.MapHealthChecks(ReadyPath, new() { Predicate = c => c.Tags.Contains(Ready) }).AllowAnonymous();

            app.Map("/health", NotFound).AllowAnonymous();
            app.Map("/health/{**rest}", NotFound).AllowAnonymous();

            return app;
        }
    }

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

    public sealed class LocatorTemplateHealthCheck(IConfiguration configuration) : IHealthCheck
    {
        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            var directory = LocatorTemplates.ResolveDirectory(configuration);

            if (!Directory.Exists(directory))
                return Task.FromResult(HealthCheckResult.Unhealthy($"{directory} does not exist, so every jar download 503s"));

            var jars = Directory.EnumerateFiles(directory, "*.jar").ToList();

            if (jars.Count == 0)
                return Task.FromResult(HealthCheckResult.Unhealthy($"{directory} holds no template jars, so every jar download 503s"));

            return Task.FromResult(IsStale(directory)
                ? HealthCheckResult.Degraded(
                    $"{directory} was built from different sources than the tree beside it, so the served "
                    + "locator is not the one in this checkout. Run `cd src/HOPPER.Locator && ./gradlew templates`.")
                : HealthCheckResult.Healthy($"{directory} holds {jars.Count} template(s)"));
        }

        /// Only where the sources are on disk, so never in the container - see docs/locator.md.
        private static bool IsStale(string templateDirectory)
        {
            // Resolved: the unresolved form still spells out build/templates/../.. .
            var sources = Path.GetFullPath(Path.Combine(templateDirectory, "..", ".."));
            if (!Directory.Exists(Path.Combine(sources, "hopper-core")))
                return false;

            var stamp = Path.Combine(templateDirectory, LocatorSourceDigest.StampFileName);
            if (!File.Exists(stamp))
                return true;

            return !string.Equals(
                File.ReadAllText(stamp).Trim(),
                LocatorSourceDigest.Of(sources),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
