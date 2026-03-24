namespace WorldServerV2.World.Stats;

/// <summary>
/// A single career base stat value — one (StatId, value) pair from the
/// <c>characterinfo_stats</c> table.
/// </summary>
/// <param name="Stat">The stat identifier (typically IDs 1–15).</param>
/// <param name="Value">The base value for this stat at the given career/level.</param>
public readonly record struct CareerStatEntry(StatId Stat, ushort Value);
