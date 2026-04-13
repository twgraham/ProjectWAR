using Core.GameWorld.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using WorldServerV2.Config;

namespace WorldServerV2.Telemetry;

/// <summary>
/// Extension methods for wiring up the OpenTelemetry metrics pipeline.
/// </summary>
public static class TelemetryServiceExtensions
{
    /// <summary>
    /// Adds OpenTelemetry metrics to the DI container:
    /// <list type="bullet">
    ///   <item><see cref="WorldServerMetrics"/> singleton — custom game-server instruments.</item>
    ///   <item>.NET runtime meters — GC, thread pool, exception counts, and more.</item>
    ///   <item>OTLP exporter — endpoint, protocol, and export interval are read from
    ///     <c>openTelemetry:otlp</c>.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddWorldServerTelemetry(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Custom metrics singleton — consumed by RegionManager → Region.
        services.AddSingleton<IWorldServerMetrics, WorldServerMetrics>();

        var otlpSection = configuration.GetSection("openTelemetry:otlp");

        var otlpEndpoint = otlpSection["endpoint"]
            ?? throw new ConfigurationException("Missing or invalid OpenTelemetry configuration section.");

        // "grpc" (default) or "httpprotobuf" — must match what your collector listener expects.
        var protocolRaw = otlpSection["protocol"] ?? "grpc";
        var protocol = protocolRaw.Equals("httpprotobuf", StringComparison.OrdinalIgnoreCase)
            ? OtlpExportProtocol.HttpProtobuf
            : OtlpExportProtocol.Grpc;

        // How often to push metrics. 60 s is the SDK default; 10 s is friendlier during dev.
        var exportIntervalSeconds = otlpSection.GetValue("exportIntervalSeconds", defaultValue: 10);

        services.AddOpenTelemetry()
            .ConfigureResource(r => r
                .AddService(
                    serviceName: "WorldServerV2",
                    serviceVersion: "1.0.0"))
            .WithMetrics(metrics =>
            {
                metrics
                    // ── Custom histogram views ─────────────────────────────────────────────
                    //
                    // The SDK default boundaries [0, 5, 10, 25, 50 … 10000] are sized for
                    // HTTP/RPC operations. Region ticks are sub-millisecond under normal load
                    // and must not exceed 50 ms (the tick budget), so we need much finer
                    // resolution at the low end.
                    .AddView(
                        instrumentName: "region.tick.duration",
                        new ExplicitBucketHistogramConfiguration
                        {
                            // Boundaries in milliseconds: 0.01 ms → 100 ms
                            Boundaries = [0.01, 0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10, 25, 50, 100]
                        })
                    .AddView(
                        instrumentName: "region.tick.budget_utilization",
                        new ExplicitBucketHistogramConfiguration
                        {
                            // Dimensionless ratio: 0 = idle, 1.0 = full budget, >1 = overrun
                            Boundaries = [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.25, 1.5, 2.0]
                        })
                    // ── Meters ────────────────────────────────────────────────────────────
                    .AddMeter(WorldServerMetrics.MeterName)
                    // .NET runtime meters: gc, threadpool, jit, exceptions …
                    .AddRuntimeInstrumentation()
                    // Push to an OTLP-compatible receiver (Grafana Alloy, OTel Collector, …)
                    .AddOtlpExporter((otlp, reader) =>
                    {
                        otlp.Endpoint = new Uri(otlpEndpoint);
                        otlp.Protocol = protocol;
                        reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                            exportIntervalSeconds * 1_000;
                    });
            });

        return services;
    }
}
