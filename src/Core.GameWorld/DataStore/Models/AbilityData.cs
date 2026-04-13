using System.Collections.Frozen;
using Core.GameWorld.Combat.Abilities;

namespace Core.GameWorld.DataStore.Models;

/// <summary>
/// Immutable bundle of ability definitions loaded from the <c>abilities</c> table.
/// <para>
/// Provides O(1) lookup by entry and pre-indexed lists per career line.
/// Career indexing uses the <see cref="AbilityDefinition.CareerLine"/> bitmask field:
/// if bit <c>(careerLine - 1)</c> is set, the ability belongs to that career.
/// </para>
/// </summary>
public sealed class AbilityData
{
    /// <summary>All abilities keyed by entry ID.</summary>
    public FrozenDictionary<ushort, AbilityDefinition> ByEntry { get; }

    /// <summary>
    /// Core (non-mastery) abilities per career line, pre-filtered and sorted by <c>MinimumRank</c>.
    /// Key: career line (1–24). Value: abilities where <c>MasteryTree == 0</c>.
    /// </summary>
    public FrozenDictionary<byte, AbilityDefinition[]> CoreAbilitiesByCareer { get; }

    /// <summary>
    /// Mastery abilities per career line, grouped by tree.
    /// Key: career line (1–24). Value: abilities where <c>MasteryTree &gt; 0</c>,
    /// ordered by tree then <c>PointCost</c>.
    /// </summary>
    public FrozenDictionary<byte, AbilityDefinition[]> MasteryAbilitiesByCareer { get; }

    public AbilityData(
        FrozenDictionary<ushort, AbilityDefinition> byEntry,
        FrozenDictionary<byte, AbilityDefinition[]> coreAbilitiesByCareer,
        FrozenDictionary<byte, AbilityDefinition[]> masteryAbilitiesByCareer)
    {
        ByEntry = byEntry;
        CoreAbilitiesByCareer = coreAbilitiesByCareer;
        MasteryAbilitiesByCareer = masteryAbilitiesByCareer;
    }

    /// <summary>
    /// Returns the core abilities available to the given career at the given level (inclusive).
    /// </summary>
    public ReadOnlySpan<AbilityDefinition> GetCoreAbilities(byte careerLine) =>
        CoreAbilitiesByCareer.TryGetValue(careerLine, out var list) ? list.AsSpan() : [];

    /// <summary>
    /// Returns all mastery abilities for the given career line.
    /// </summary>
    public ReadOnlySpan<AbilityDefinition> GetMasteryAbilities(byte careerLine) =>
        MasteryAbilitiesByCareer.TryGetValue(careerLine, out var list) ? list.AsSpan() : [];

    /// <summary>
    /// Looks up a single ability by entry ID.
    /// </summary>
    public AbilityDefinition? GetByEntry(ushort entry) =>
        ByEntry.TryGetValue(entry, out var def) ? def : null;

    /// <summary>An empty instance with no ability data.</summary>
    public static AbilityData Empty { get; } = new(
        FrozenDictionary<ushort, AbilityDefinition>.Empty,
        FrozenDictionary<byte, AbilityDefinition[]>.Empty,
        FrozenDictionary<byte, AbilityDefinition[]>.Empty);
}
