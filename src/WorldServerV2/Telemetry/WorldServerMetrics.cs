using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace WorldServerV2.Telemetry;

/// <summary>
/// Central registry of all custom metrics for WorldServerV2.
/// <para>
/// Consumers should inject this as a singleton and call the <c>Record*</c> helpers rather
/// than accessing individual instruments directly. This keeps instrument creation in one
/// place and prevents accidental duplication.
/// </para>
/// </summary>
public sealed class WorldServerMetrics : IDisposable
{
    /// <summary>Name of the application meter — used as the meter name in the OTel pipeline.</summary>
    public const string MeterName = "WorldServer";

    private readonly Meter _meter;

    // ── Region tick metrics ────────────────────────────────────────────────────

    /// <summary>
    /// Wall-clock duration of a single region tick in milliseconds.
    /// High-resolution — resolves to sub-millisecond granularity via
    /// <see cref="Stopwatch.GetElapsedTime"/>.
    /// </summary>
    private readonly Histogram<double> _tickDurationMs;

    /// <summary>
    /// Fraction of the tick budget consumed (0.0 = idle, 1.0 = full budget, >1.0 = overrun).
    /// Budget is defined by <c>RegionConstants.TickInterval</c> (50 ms at 20 Hz).
    /// </summary>
    private readonly Histogram<double> _tickBudgetUtilization;

    public WorldServerMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _tickDurationMs = _meter.CreateHistogram<double>(
            name: "region.tick.duration",
            unit: "ms",
            description: "Wall-clock duration of a single region tick.");

        _tickBudgetUtilization = _meter.CreateHistogram<double>(
            name: "region.tick.budget_utilization",
            unit: "1",
            description: "Fraction of the 50 ms tick budget consumed per tick (1.0 = 100 %).");
    }

    /// <summary>
    /// Records a completed tick. Should be called after <c>Tick()</c> returns and
    /// before <c>Thread.Sleep</c> consumes the remainder of the budget.
    /// </summary>
    /// <param name="elapsed">Actual time the tick took.</param>
    /// <param name="budget">The target tick interval (e.g. 50 ms).</param>
    /// <param name="tags">Dimension tags — include at minimum <c>region_id</c>.</param>
    public void RecordTick(TimeSpan elapsed, TimeSpan budget, in TagList tags)
    {
        _tickDurationMs.Record(elapsed.TotalMilliseconds, tags);
        _tickBudgetUtilization.Record(elapsed / budget, tags);
    }

    public void Dispose() => _meter.Dispose();
}
