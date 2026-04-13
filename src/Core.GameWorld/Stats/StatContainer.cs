namespace Core.GameWorld.Stats;

/// <summary>
/// Per-entity stat storage. Wraps a fixed <see cref="StatEntry"/>[109] array indexed
/// by <see cref="StatId"/>. Provides the public API for all stat mutations and reads.
/// <para>
/// <b>Dirty flag</b>: any mutation sets <see cref="IsDirty"/> to <c>true</c>.
/// <see cref="Flush"/> recomputes derived stats and resets the flag.
/// <c>GetTotal()</c> always returns the live value — the dirty flag gates
/// <em>client notification</em>, not internal reads.
/// </para>
/// <para>
/// <b>Threading</b>: not thread-safe. All mutation and Flush() must happen on the
/// owning region's tick thread. Advisory reads from handler threads see the last
/// consistent value and are re-validated on execution.
/// </para>
/// </summary>
public sealed class StatContainer
{
    private readonly StatEntry[] _entries;

    /// <summary>
    /// Callback invoked by <see cref="Flush"/> when max-health changes.
    /// The parameter is the new max-health value.  Set by UnitEntity to update
    /// <c>HealthComponent.Max</c> without creating a bidirectional dependency.
    /// </summary>
    public Action<uint>? OnMaxHealthChanged { get; set; }

    public StatContainer()
    {
        _entries = new StatEntry[StatConstants.SlotCount];
        for (int i = 0; i < _entries.Length; i++)
            _entries[i] = new StatEntry();
    }

    // ── Dirty tracking ───────────────────────────────────────────────────

    /// <summary>
    /// <c>true</c> if any modifier has been mutated since the last <see cref="Flush"/>.
    /// </summary>
    public bool IsDirty { get; private set; }

    /// <summary>Marks the container as dirty (e.g. after bulk initialization).</summary>
    public void MarkDirty() => IsDirty = true;

    // ── Reads ────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the final computed value for the given stat.
    /// Base stats (IDs 1–16) are floored at 0; everything else can go negative.
    /// </summary>
    public int GetTotal(StatId stat)
    {
        bool floor = StatConstants.IsBaseStat(stat);
        return _entries[(int)stat].GetTotal(floor);
    }

    /// <summary>
    /// Direct access to the <see cref="StatEntry"/> for advanced callers
    /// (e.g. bolster logic, item equip). Avoid holding references across ticks.
    /// </summary>
    public StatEntry this[StatId stat] => _entries[(int)stat];

    // ── Base / Renown / Item layer setters ────────────────────────────────

    /// <summary>Sets the base stat value (from level tables / DB).</summary>
    public void SetBase(StatId stat, int value)
    {
        _entries[(int)stat].Base = value;
        IsDirty = true;
    }

    /// <summary>Sets the renown bonus for the given stat.</summary>
    public void SetRenown(StatId stat, int value)
    {
        _entries[(int)stat].Renown = value;
        IsDirty = true;
    }

    /// <summary>Sets the item bonus for the given stat.</summary>
    public void SetItemBonus(StatId stat, int value)
    {
        _entries[(int)stat].ItemBonus = value;
        IsDirty = true;
    }

    /// <summary>Sets bolster scaling factor for a stat's item bonus layer.</summary>
    public void SetBolsterFactor(StatId stat, float factor)
    {
        _entries[(int)stat].BolsterFactor = factor;
        IsDirty = true;
    }

    /// <summary>Disables or enables the item bonus contribution for a stat.</summary>
    public void SetItemBonusDisabled(StatId stat, bool disabled)
    {
        _entries[(int)stat].ItemBonusDisabled = disabled;
        IsDirty = true;
    }

    // ── Buff additive bonus / reduction ──────────────────────────────────

    /// <summary>Adds a flat bonus from a buff of the given class.</summary>
    public void AddBonus(StatId stat, int value, BuffClass source)
    {
        _entries[(int)stat].AddBonus(value, source);
        IsDirty = true;
    }

    /// <summary>Removes a previously-added flat bonus.</summary>
    public void RemoveBonus(StatId stat, int value, BuffClass source)
    {
        _entries[(int)stat].RemoveBonus(value, source);
        IsDirty = true;
    }

    /// <summary>Adds a flat reduction (debuff) from the given buff class.</summary>
    public void AddReduction(StatId stat, int value, BuffClass source)
    {
        _entries[(int)stat].AddReduction(value, source);
        IsDirty = true;
    }

    /// <summary>Removes a previously-added flat reduction.</summary>
    public void RemoveReduction(StatId stat, int value, BuffClass source)
    {
        _entries[(int)stat].RemoveReduction(value, source);
        IsDirty = true;
    }

    // ── Buff percentage multipliers ──────────────────────────────────────

    /// <summary>
    /// Adds a percentage bonus multiplier (1.25 = +25%) from the given buff class.
    /// </summary>
    public void AddBonusMultiplier(StatId stat, float fraction, BuffClass source)
    {
        _entries[(int)stat].AddBonusMultiplier(fraction, source);
        IsDirty = true;
    }

    /// <summary>Removes a previously-added percentage bonus multiplier.</summary>
    public void RemoveBonusMultiplier(StatId stat, float fraction, BuffClass source)
    {
        _entries[(int)stat].RemoveBonusMultiplier(fraction, source);
        IsDirty = true;
    }

    /// <summary>
    /// Adds a percentage reduction multiplier (0.8 = reduce to 80%) from the given buff class.
    /// </summary>
    public void AddReductionMultiplier(StatId stat, float fraction, BuffClass source)
    {
        _entries[(int)stat].AddReductionMultiplier(fraction, source);
        IsDirty = true;
    }

    /// <summary>Removes a previously-added percentage reduction multiplier.</summary>
    public void RemoveReductionMultiplier(StatId stat, float fraction, BuffClass source)
    {
        _entries[(int)stat].RemoveReductionMultiplier(fraction, source);
        IsDirty = true;
    }

    // ── Flush (once per tick) ────────────────────────────────────────────

    /// <summary>
    /// Recomputes derived stats and resets the dirty flag.
    /// Call at most once per tick, after all mutations for the tick are complete.
    /// <para>
    /// Currently recomputes:
    /// <list type="bullet">
    ///   <item><c>MaxHealth = Wounds × 10</c> (notifies via <see cref="OnMaxHealthChanged"/>)</item>
    /// </list>
    /// Additional derived stats (action points, etc.) will be added as their
    /// consuming systems come online.
    /// </para>
    /// </summary>
    public void Flush()
    {
        if (!IsDirty)
            return;

        IsDirty = false;

        // ── Derived: MaxHealth from Wounds ───────────────────────────────
        int wounds = GetTotal(StatId.Wounds);
        uint maxHealth = (uint)Math.Max(1, wounds * 10);
        OnMaxHealthChanged?.Invoke(maxHealth);
    }

    // ── Bulk operations ──────────────────────────────────────────────────

    /// <summary>
    /// Clears all modifier layers on all stats. Base and Renown values are preserved.
    /// Marks as dirty.
    /// </summary>
    public void ClearAllModifiers()
    {
        for (int i = 0; i < _entries.Length; i++)
            _entries[i].ClearModifiers();
        IsDirty = true;
    }

    /// <summary>
    /// Resets every stat entry to default (all layers zeroed). Used during full
    /// character re-initialization.
    /// </summary>
    public void ResetAll()
    {
        for (int i = 0; i < _entries.Length; i++)
            _entries[i] = new StatEntry();
        IsDirty = true;
    }
}
