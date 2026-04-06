namespace Core.GameWorld.Stats;

/// <summary>
/// Per-stat modifier state. Manages five independent modifier layers and computes
/// the final stat value on demand.
/// <para>
/// <b>Formula</b>:
/// <c>Total = (Base + Renown + ItemBonus + BuffBonus − BuffReduction) × (BonusMult × ReductionMult)</c>
/// </para>
/// <para>
/// <b>Threading</b>: not thread-safe. All mutation must happen on the owning
/// region's tick thread. Reads from external threads (handler validation) see the
/// last consistent value written by the region thread — this is acceptable because
/// external reads are advisory (abilities re-validate on execution).
/// </para>
/// </summary>
public sealed class StatEntry
{
    // ── Additive modifier tracking per buff class ────────────────────────
    //
    // For "highest only" classes (Buff0, Buff1): we track all applied values
    // in sorted order so when the highest is removed, the next-highest takes over.
    // For additive classes (Tactic, Career): we just sum.
    //
    // Implementation: per-class bonus and reduction totals. Highest-only classes
    // use sorted lists; additive classes use simple accumulators.

    private readonly int[] _bonusPerClass = new int[BuffClassConstants.ClassCount];
    private readonly int[] _reductionPerClass = new int[BuffClassConstants.ClassCount];

    // Sorted-descending lists for highest-only classes.
    // Allocated lazily (most stat entries are never modified by buff class 0/1).
    private List<int>?[] _bonusSorted = new List<int>?[BuffClassConstants.ClassCount];
    private List<int>?[] _reductionSorted = new List<int>?[BuffClassConstants.ClassCount];

    // Multipliers per class. Stored as product of all applied multipliers.
    // Starts at 1.0 — "no modification".
    private readonly float[] _bonusMult = [1f, 1f, 1f, 1f];
    private readonly float[] _reductionMult = [1f, 1f, 1f, 1f];

    // Sorted lists for highest-only multiplier classes.
    private List<float>?[] _bonusMultSorted = new List<float>?[BuffClassConstants.ClassCount];
    private List<float>?[] _reductionMultSorted = new List<float>?[BuffClassConstants.ClassCount];

    // ── Layer 1: Base stat (from level tables / DB) ──────────────────────

    /// <summary>Base stat value from character level/race tables.</summary>
    public int Base { get; set; }

    // ── Layer 2: Renown bonus ────────────────────────────────────────────

    /// <summary>Bonus from renown spec allocation.</summary>
    public int Renown { get; set; }

    // ── Layer 3: Item bonus ──────────────────────────────────────────────

    /// <summary>
    /// Raw item bonus (gear + talisman). Subject to bolster scaling via
    /// <see cref="BolsterFactor"/> and can be disabled entirely.
    /// </summary>
    public int ItemBonus { get; set; }

    /// <summary>
    /// Bolster scaling factor applied to <see cref="ItemBonus"/> (default 1.0 = no scaling).
    /// Set by RvR bolster logic.
    /// </summary>
    public float BolsterFactor { get; set; } = 1f;

    /// <summary>If true, <see cref="ItemBonus"/> contributes 0 (e.g. during stealth).</summary>
    public bool ItemBonusDisabled { get; set; }

    // ── Computed values ──────────────────────────────────────────────────

    /// <summary>
    /// Effective item contribution after bolster and disable checks.
    /// </summary>
    public int EffectiveItemBonus =>
        ItemBonusDisabled ? 0 : (int)(ItemBonus * BolsterFactor);

    /// <summary>
    /// Net additive modifier: item + buff bonuses − buff reductions.
    /// </summary>
    public int LinearModifier
    {
        get
        {
            int bonus = EffectiveItemBonus;
            int reduction = 0;
            for (int i = 0; i < BuffClassConstants.ClassCount; i++)
            {
                bonus += _bonusPerClass[i];
                reduction += _reductionPerClass[i];
            }
            return bonus - reduction;
        }
    }

    /// <summary>
    /// Combined percentage multiplier (all buff classes).
    /// </summary>
    public float TotalMultiplier
    {
        get
        {
            float bonus = 1f;
            float reduction = 1f;
            for (int i = 0; i < BuffClassConstants.ClassCount; i++)
            {
                bonus *= _bonusMult[i];
                reduction *= _reductionMult[i];
            }
            return bonus * reduction;
        }
    }

    /// <summary>
    /// Final computed stat value:
    /// <c>(Base + Renown + LinearModifier) × TotalMultiplier</c>.
    /// Floored at 0 for base stats (callers can check themselves for non-base stats
    /// if negative values are meaningful).
    /// </summary>
    /// <param name="floorAtZero">
    /// If <c>true</c>, the result is clamped to a minimum of 0.
    /// Base stats (IDs 1–16) should always floor; modifier stats may not.
    /// </param>
    public int GetTotal(bool floorAtZero = false)
    {
        int raw = (int)((Base + Renown + LinearModifier) * TotalMultiplier);
        return floorAtZero ? Math.Max(0, raw) : raw;
    }

    // ── Additive bonus/reduction mutations ───────────────────────────────

    /// <summary>
    /// Adds a flat bonus from the given buff class.
    /// </summary>
    public void AddBonus(int value, BuffClass source)
    {
        int idx = (int)source;
        if (BuffClassConstants.IsHighestOnly(source))
        {
            var list = _bonusSorted[idx] ??= [];
            InsertDescending(list, value);
            _bonusPerClass[idx] = list[0]; // highest
        }
        else
        {
            _bonusPerClass[idx] += value;
        }
    }

    /// <summary>
    /// Removes a previously-added flat bonus from the given buff class.
    /// </summary>
    public void RemoveBonus(int value, BuffClass source)
    {
        int idx = (int)source;
        if (BuffClassConstants.IsHighestOnly(source))
        {
            var list = _bonusSorted[idx];
            if (list is null) return;
            list.Remove(value);
            _bonusPerClass[idx] = list.Count > 0 ? list[0] : 0;
        }
        else
        {
            _bonusPerClass[idx] -= value;
        }
    }

    /// <summary>
    /// Adds a flat reduction (debuff) from the given buff class.
    /// </summary>
    public void AddReduction(int value, BuffClass source)
    {
        int idx = (int)source;
        if (BuffClassConstants.IsHighestOnly(source))
        {
            var list = _reductionSorted[idx] ??= [];
            InsertDescending(list, value);
            _reductionPerClass[idx] = list[0]; // highest
        }
        else
        {
            _reductionPerClass[idx] += value;
        }
    }

    /// <summary>
    /// Removes a previously-added flat reduction from the given buff class.
    /// </summary>
    public void RemoveReduction(int value, BuffClass source)
    {
        int idx = (int)source;
        if (BuffClassConstants.IsHighestOnly(source))
        {
            var list = _reductionSorted[idx];
            if (list is null) return;
            list.Remove(value);
            _reductionPerClass[idx] = list.Count > 0 ? list[0] : 0;
        }
        else
        {
            _reductionPerClass[idx] -= value;
        }
    }

    // ── Percentage multiplier mutations ──────────────────────────────────

    /// <summary>
    /// Adds a percentage bonus multiplier. The <paramref name="fraction"/> is centered
    /// on 1.0: pass 1.25 for "+25%", 0.8 for "−20%".
    /// </summary>
    public void AddBonusMultiplier(float fraction, BuffClass source)
    {
        int idx = (int)source;
        if (BuffClassConstants.IsHighestOnly(source))
        {
            var list = _bonusMultSorted[idx] ??= [];
            InsertDescending(list, fraction);
            _bonusMult[idx] = list[0]; // highest
        }
        else
        {
            _bonusMult[idx] *= fraction;
        }
    }

    /// <summary>
    /// Removes a previously-added percentage bonus multiplier.
    /// </summary>
    public void RemoveBonusMultiplier(float fraction, BuffClass source)
    {
        int idx = (int)source;
        if (BuffClassConstants.IsHighestOnly(source))
        {
            var list = _bonusMultSorted[idx];
            if (list is null) return;
            list.Remove(fraction);
            _bonusMult[idx] = list.Count > 0 ? list[0] : 1f;
        }
        else
        {
            // Guard against division by zero/near-zero.
            if (Math.Abs(fraction) < 1e-6f) return;
            _bonusMult[idx] /= fraction;
        }
    }

    /// <summary>
    /// Adds a percentage reduction multiplier (e.g. 0.8 = "reduce to 80%").
    /// </summary>
    public void AddReductionMultiplier(float fraction, BuffClass source)
    {
        int idx = (int)source;
        if (BuffClassConstants.IsHighestOnly(source))
        {
            var list = _reductionMultSorted[idx] ??= [];
            // For reductions, the "strongest" is the smallest value (0.5 > 0.8 as a reduction).
            InsertAscending(list, fraction);
            _reductionMult[idx] = list[0]; // smallest = strongest reduction
        }
        else
        {
            _reductionMult[idx] *= fraction;
        }
    }

    /// <summary>
    /// Removes a previously-added percentage reduction multiplier.
    /// </summary>
    public void RemoveReductionMultiplier(float fraction, BuffClass source)
    {
        int idx = (int)source;
        if (BuffClassConstants.IsHighestOnly(source))
        {
            var list = _reductionMultSorted[idx];
            if (list is null) return;
            list.Remove(fraction);
            _reductionMult[idx] = list.Count > 0 ? list[0] : 1f;
        }
        else
        {
            if (Math.Abs(fraction) < 1e-6f) return;
            _reductionMult[idx] /= fraction;
        }
    }

    // ── Reset ────────────────────────────────────────────────────────────

    /// <summary>
    /// Clears all modifier layers. Base and Renown are preserved.
    /// </summary>
    public void ClearModifiers()
    {
        ItemBonus = 0;
        BolsterFactor = 1f;
        ItemBonusDisabled = false;

        Array.Clear(_bonusPerClass);
        Array.Clear(_reductionPerClass);
        Array.Fill(_bonusMult, 1f);
        Array.Fill(_reductionMult, 1f);

        for (int i = 0; i < BuffClassConstants.ClassCount; i++)
        {
            _bonusSorted[i]?.Clear();
            _reductionSorted[i]?.Clear();
            _bonusMultSorted[i]?.Clear();
            _reductionMultSorted[i]?.Clear();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>Insert into a list maintaining descending order.</summary>
    private static void InsertDescending(List<int> list, int value)
    {
        int idx = list.BinarySearch(value, ReverseIntComparer.Instance);
        if (idx < 0) idx = ~idx;
        list.Insert(idx, value);
    }

    /// <summary>Insert into a list maintaining ascending order.</summary>
    private static void InsertAscending(List<float> list, float value)
    {
        int idx = list.BinarySearch(value);
        if (idx < 0) idx = ~idx;
        list.Insert(idx, value);
    }

    /// <summary>Insert into a list maintaining descending order.</summary>
    private static void InsertDescending(List<float> list, float value)
    {
        int idx = list.BinarySearch(value, ReverseFloatComparer.Instance);
        if (idx < 0) idx = ~idx;
        list.Insert(idx, value);
    }

    /// <summary>Comparer that sorts integers in descending order.</summary>
    private sealed class ReverseIntComparer : IComparer<int>
    {
        public static readonly ReverseIntComparer Instance = new();
        public int Compare(int x, int y) => y.CompareTo(x);
    }

    /// <summary>Comparer that sorts floats in descending order.</summary>
    private sealed class ReverseFloatComparer : IComparer<float>
    {
        public static readonly ReverseFloatComparer Instance = new();
        public int Compare(float x, float y) => y.CompareTo(x);
    }
}
