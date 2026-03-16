namespace WorldServerV2.Data.Entities;

/// <summary>
/// A recorded scenario run duration, mapped to the <c>scenario_durations</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class ScenarioDuration
{
    /// <summary>Auto-generated identity PK.</summary>
    public int Guid { get; set; }
    public ushort? ScenarioId { get; set; }
    public byte? Tier { get; set; }
    /// <summary>Start time as a raw epoch value (unit may be milliseconds or seconds).</summary>
    public long? StartTime { get; set; }
    public uint? DurationSeconds { get; set; }
}
