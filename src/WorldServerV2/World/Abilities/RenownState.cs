using System.Globalization;

namespace WorldServerV2.World.Abilities;

/// <summary>
/// Immutable representation of a player's renown skill state.
/// <para>
/// Parses the legacy DB string format:
/// <c>"tree:pos;tree:pos;..."</c> (trailing semicolon)
/// where <c>tree</c> is a 0-based tree index (0–6) and <c>pos</c> is a
/// 0-based position within that tree (0–19).
/// </para>
/// The raw string is never sent to the client — it is purely a DB persistence format.
/// </summary>
public sealed class RenownState
{
    /// <summary>Number of renown trees.</summary>
    public const int TreeCount = 7;

    /// <summary>Maximum number of skill slots per renown tree.</summary>
    public const int MaxSlotsPerTree = 20;

    /// <summary>
    /// Each trained renown slot, stored as <c>(tree 0–6, position 0–19)</c>.
    /// Ordered by tree then position for deterministic serialization.
    /// </summary>
    public IReadOnlyList<(byte Tree, byte Position)> TrainedSlots { get; }

    /// <summary>Total renown points spent (sum of each slot's cost — not stored here, must be resolved externally).</summary>
    public int SlotCount => TrainedSlots.Count;

    private RenownState(IReadOnlyList<(byte Tree, byte Position)> trainedSlots)
    {
        TrainedSlots = trainedSlots;
    }

    /// <summary>
    /// Returns whether the given slot has been trained.
    /// </summary>
    public bool IsTrained(int tree, int position)
    {
        foreach (var (t, p) in TrainedSlots)
        {
            if (t == tree && p == position)
                return true;
        }
        return false;
    }

    /// <summary>An empty renown state with no trained slots.</summary>
    public static RenownState Empty { get; } = new([]);

    /// <summary>
    /// Parses the legacy DB string into a <see cref="RenownState"/>.
    /// Returns <see cref="Empty"/> for null, empty, or malformed strings.
    /// </summary>
    public static RenownState Parse(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return Empty;

        var entries = raw.Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (entries.Length == 0)
            return Empty;

        var slots = new List<(byte Tree, byte Position)>(entries.Length);

        foreach (var entry in entries)
        {
            var colonIdx = entry.IndexOf(':');
            if (colonIdx < 0)
                continue; // skip malformed entries

            if (!byte.TryParse(entry.AsSpan(0, colonIdx), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tree))
                continue;
            if (!byte.TryParse(entry.AsSpan(colonIdx + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pos))
                continue;

            if (tree >= TreeCount || pos >= MaxSlotsPerTree)
                continue;

            slots.Add((tree, pos));
        }

        // Sort for deterministic serialization
        slots.Sort((a, b) =>
        {
            var cmp = a.Tree.CompareTo(b.Tree);
            return cmp != 0 ? cmp : a.Position.CompareTo(b.Position);
        });

        return slots.Count > 0 ? new RenownState(slots) : Empty;
    }

    /// <summary>
    /// Serializes back to the legacy DB string format (trailing semicolon).
    /// </summary>
    public string Serialize()
    {
        if (TrainedSlots.Count == 0)
            return string.Empty;

        // Each entry is "T:P;" — max 5 chars per entry
        var sb = new System.Text.StringBuilder(TrainedSlots.Count * 5);
        foreach (var (tree, pos) in TrainedSlots)
        {
            sb.Append(CultureInfo.InvariantCulture, $"{tree}:{pos};");
        }
        return sb.ToString();
    }
}
