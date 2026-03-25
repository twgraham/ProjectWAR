using System.Globalization;

namespace WorldServerV2.World.Abilities;

/// <summary>
/// Immutable representation of a player's mastery specialization state.
/// <para>
/// Parses the legacy DB string format:
/// <c>"pts1;pts2;pts3;s1,s2,...,s7;s1,s2,...,s7;s1,s2,...,s7"</c>
/// where <c>pts1..3</c> are base points per tree (excluding gear bonuses)
/// and each <c>s1..s7</c> is a <c>0</c>/<c>1</c> flag indicating whether
/// a mastery skill slot is activated.
/// </para>
/// The raw string is never sent to the client — it is purely a DB persistence format.
/// </summary>
public sealed class MasteryState
{
    /// <summary>Number of mastery trees per career.</summary>
    public const int TreeCount = 3;

    /// <summary>Number of skill slots per mastery tree.</summary>
    public const int SlotsPerTree = 7;

    /// <summary>Total number of semicolon-separated segments in the DB string.</summary>
    private const int TotalSegments = TreeCount + TreeCount; // 3 point segments + 3 skill segments

    /// <summary>Base points spent in each of the 3 mastery trees (excludes gear bonuses).</summary>
    public ReadOnlyMemory<byte> PointsPerTree { get; }

    /// <summary>
    /// Activation flags for each skill slot: <c>[tree, slot]</c>.
    /// <c>true</c> means the skill at that position has been purchased.
    /// </summary>
    private readonly bool[] _activeSkills; // flattened [tree * SlotsPerTree + slot]

    private MasteryState(byte[] pointsPerTree, bool[] activeSkills)
    {
        PointsPerTree = pointsPerTree;
        _activeSkills = activeSkills;
    }

    /// <summary>Gets the base points (excluding gear bonuses) spent in the given tree (0-based).</summary>
    public byte GetTreePoints(int tree)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tree);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(tree, TreeCount);
        return PointsPerTree.Span[tree];
    }

    /// <summary>Returns whether the skill at <paramref name="slot"/> in <paramref name="tree"/> is active.</summary>
    public bool IsSkillActive(int tree, int slot)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(tree);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(tree, TreeCount);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(slot, SlotsPerTree);
        return _activeSkills[tree * SlotsPerTree + slot];
    }

    /// <summary>Total mastery points spent across all three trees.</summary>
    public int TotalPointsSpent
    {
        get
        {
            var span = PointsPerTree.Span;
            return span[0] + span[1] + span[2];
        }
    }

    /// <summary>
    /// An empty mastery state: zero points, no skills active.
    /// </summary>
    public static MasteryState Empty { get; } = new(new byte[TreeCount], new bool[TreeCount * SlotsPerTree]);

    /// <summary>
    /// Parses the legacy DB string into a <see cref="MasteryState"/>.
    /// Returns <see cref="Empty"/> for null, empty, or malformed strings.
    /// </summary>
    public static MasteryState Parse(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw.Length < 2)
            return Empty;

        var segments = raw.Split(';');
        if (segments.Length < TotalSegments)
            return Empty;

        var points = new byte[TreeCount];
        for (var i = 0; i < TreeCount; i++)
        {
            if (!byte.TryParse(segments[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var pts))
                return Empty;
            points[i] = pts;
        }

        var skills = new bool[TreeCount * SlotsPerTree];
        for (var tree = 0; tree < TreeCount; tree++)
        {
            var slotSegment = segments[tree + TreeCount].Split(',');
            if (slotSegment.Length < SlotsPerTree)
                return Empty;

            for (var slot = 0; slot < SlotsPerTree; slot++)
            {
                if (!byte.TryParse(slotSegment[slot], NumberStyles.Integer, CultureInfo.InvariantCulture, out var flag))
                    return Empty;
                skills[tree * SlotsPerTree + slot] = flag != 0;
            }
        }

        return new MasteryState(points, skills);
    }

    /// <summary>
    /// Serializes back to the legacy DB string format.
    /// </summary>
    public string Serialize()
    {
        var span = PointsPerTree.Span;
        // Pre-calculate: "p;p;p;s,s,s,s,s,s,s;s,s,s,s,s,s,s;s,s,s,s,s,s,s"
        // Max length: 3*3 + 3 + 3*(7*1 + 6) + 2 ≈ 55
        return string.Create(CultureInfo.InvariantCulture,
            $"{span[0]};{span[1]};{span[2]};" +
            $"{B(0, 0)},{B(0, 1)},{B(0, 2)},{B(0, 3)},{B(0, 4)},{B(0, 5)},{B(0, 6)};" +
            $"{B(1, 0)},{B(1, 1)},{B(1, 2)},{B(1, 3)},{B(1, 4)},{B(1, 5)},{B(1, 6)};" +
            $"{B(2, 0)},{B(2, 1)},{B(2, 2)},{B(2, 3)},{B(2, 4)},{B(2, 5)},{B(2, 6)}");

        int B(int tree, int slot) => _activeSkills[tree * SlotsPerTree + slot] ? 1 : 0;
    }
}
