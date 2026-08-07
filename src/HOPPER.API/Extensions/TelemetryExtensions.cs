using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HOPPER.API.Extensions
{
    public static class TelemetryExtensions
    {
        // Entirely opt-in. Without Otel:Endpoint nothing is registered at all, rather than an
        // exporter retrying against a collector that is not there on a self-hosted single instance.
        public static IServiceCollection AddHopperTelemetry(this IServiceCollection services, IConfiguration configuration)
        {
            if (configuration["Otel:Endpoint"] is not { Length: > 0 } endpoint)
                return services;

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
                throw new InvalidOperationException($"Otel:Endpoint is not an absolute URL: {endpoint}");

            var serviceName = configuration["Otel:ServiceName"] is { Length: > 0 } named ? named : "hopper";

            services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService(serviceName))
                .WithTracing(tracing => tracing
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        // The probes are the loudest requests an idle instance makes, and a trace
                        // per probe buries the ones that mean something.
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health", StringComparison.Ordinal);
                    })
                    .AddHttpClientInstrumentation()
                    .AddOtlpExporter(o => o.Endpoint = uri))
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddOtlpExporter(o => o.Endpoint = uri));

            return services;
        }
    }
}
