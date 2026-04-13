namespace Core.GameWorld.Components;

/// <summary>
/// Manages an entity's health pool — current HP, maximum HP, and death state.
/// <para>
/// This is a required component held as a direct field on <c>UnitEntity</c>, not an
/// optional <see cref="IComponent"/> in the bag. All combat-capable entities have health
/// by construction — no dictionary lookup or null check needed.
/// </para>
/// </summary>
public sealed class HealthComponent
{
    private uint _current;
    private uint _max;

    /// <summary>
    /// Invoked when health reaches zero. The owning entity subscribes to this
    /// and translates it into a region-level death event.
    /// </summary>
    public Action? OnDied;

    public HealthComponent(uint maxHealth)
    {
        ArgumentOutOfRangeException.ThrowIfZero(maxHealth);
        _max = maxHealth;
        _current = maxHealth;
    }

    /// <summary>Current hit points. Clamped to [0, <see cref="Max"/>].</summary>
    public uint Current
    {
        get => _current;
        private set => _current = Math.Min(value, _max);
    }

    /// <summary>Maximum hit points (base + bonuses).</summary>
    public uint Max
    {
        get => _max;
        set
        {
            ArgumentOutOfRangeException.ThrowIfZero(value);
            _max = value;
            if (_current > _max)
                _current = _max;
        }
    }

    /// <summary>Current health as a percentage (0–100).</summary>
    public byte Percent => _max == 0 ? (byte)0 : (byte)(_current * 100 / _max);

    /// <summary>Whether the entity is dead (0 HP).</summary>
    public bool IsDead => _current == 0;

    /// <summary>Whether the entity is alive (HP > 0).</summary>
    public bool IsAlive => _current > 0;

    // ── Damage / Heal ───────────────────────────────────────────────────

    /// <summary>
    /// Applies damage to this entity. Returns the actual damage dealt (after clamping).
    /// Does nothing if already dead.
    /// </summary>
    public uint TakeDamage(uint amount)
    {
        if (IsDead || amount == 0)
            return 0;

        var actual = Math.Min(amount, _current);
        _current -= actual;

        if (_current == 0)
            OnDied?.Invoke();

        return actual;
    }

    /// <summary>
    /// Restores health. Returns the actual amount healed (after clamping to max).
    /// Does nothing if dead — use <see cref="Resurrect"/> first.
    /// </summary>
    public uint Heal(uint amount)
    {
        if (IsDead || amount == 0)
            return 0;

        var before = _current;
        _current = Math.Min(_current + amount, _max);
        return _current - before;
    }

    /// <summary>
    /// Resurrects the entity with the specified percentage of max health (1–100).
    /// Only works if the entity is dead.
    /// </summary>
    /// <returns><c>true</c> if the entity was resurrected.</returns>
    public bool Resurrect(byte healthPercent = 100)
    {
        if (!IsDead)
            return false;

        healthPercent = Math.Clamp(healthPercent, (byte)1, (byte)100);
        _current = _max * healthPercent / 100;
        return true;
    }
}
