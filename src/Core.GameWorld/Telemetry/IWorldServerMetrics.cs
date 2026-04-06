using System.Diagnostics;

namespace Core.GameWorld.Telemetry;

public interface IWorldServerMetrics
{
    void RecordTick(TimeSpan elapsed, TimeSpan budget, in TagList tags);
}