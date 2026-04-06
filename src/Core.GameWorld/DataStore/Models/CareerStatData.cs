using System.Collections.Frozen;
using Core.GameWorld.Stats;

namespace Core.GameWorld.DataStore.Models;

/// <summary>
/// Immutable bundle of career base-stat data loaded from the <c>characterinfo_stats</c>
/// table. Keyed by <c>(CareerLine, Level)</c>, each entry is an array of
/// <see cref="CareerStatEntry"/> values for that career at that level.
/// </summary>
/// <param name="StatsByCareerLevel">
/// Frozen lookup: <c>(byte careerLine, byte level) → CareerStatEntry[]</c>.
/// </param>
public readonly record struct CareerStatData(
    FrozenDictionary<(byte CareerLine, byte Level), CareerStatEntry[]> StatsByCareerLevel)
{
    /// <summary>
    /// Returns the base stat entries for the given career line at the given level.
    /// Returns an empty span if no data exists for the combination.
    /// </summary>
    public ReadOnlySpan<CareerStatEntry> GetBaseStats(byte careerLine, byte level) =>
        StatsByCareerLevel.TryGetValue((careerLine, level), out var stats)
            ? stats.AsSpan()
            : ReadOnlySpan<CareerStatEntry>.Empty;

    /// <summary>An empty instance with no career stat data.</summary>
    public static CareerStatData Empty { get; } =
        new(FrozenDictionary<(byte, byte), CareerStatEntry[]>.Empty);
}
