namespace Core.GameWorld.Combat.Career;

/// <summary>
/// Configuration for <see cref="ComboResource"/> — a small counter incremented
/// by abilities and consumed by finishers.
/// <para>
/// Covers: Witch Hunter/Witch Elf (Accusations/Kisses, 0–5),
/// Black Orc (Plan, 0–2 with wrap), Swordmaster (Blade Enchantment, 0–2 with wrap).
/// </para>
/// </summary>
public sealed record ComboResourceConfig
{
    /// <summary>Maximum combo count (e.g. 5 for WH/WE, 2 for BO/SM).</summary>
    public byte Max { get; init; } = 5;

    /// <summary>
    /// If <c>true</c>, consuming resets to 0 regardless of amount.
    /// WH/WE finishers consume all accusations. Set <c>false</c> for partial consume.
    /// </summary>
    public bool ConsumeAll { get; init; } = true;

    /// <summary>
    /// If <c>true</c>, incrementing past max wraps to 1 instead of clamping.
    /// Used by BO/SM 3-step combos (0 → 1 → 2 → wrap to 1).
    /// </summary>
    public bool WrapOnOverflow { get; init; }

    /// <summary>
    /// If <c>true</c>, <see cref="ComboResource.HasResource"/> checks for an exact
    /// match (used by BO/SM combo-step gating). If <c>false</c>, checks ≥ cost.
    /// </summary>
    public bool ExactMatch { get; init; }

    /// <summary>
    /// Inactivity timeout (ms) before the counter resets to 0.
    /// Set to 0 for no timeout.
    /// </summary>
    public ushort TimeoutMs { get; init; } = 20_000;
}

/// <summary>
/// A small combo counter (0–Max) incremented by abilities, consumed by finishers.
/// Optionally resets after an inactivity timeout.
/// </summary>
public sealed class ComboResource : ICareerResource
{
    private readonly ComboResourceConfig _config;

    private byte _current;
    private long _lastActionTick;

    public ComboResource(ComboResourceConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc />
    public byte Current => _current;

    /// <inheritdoc />
    public byte Max => _config.Max;

    /// <inheritdoc />
    /// <remarks>For combos, Level = Current (each point is its own level).</remarks>
    public byte Level => _current;

    /// <inheritdoc />
    public bool HasResource(int cost)
    {
        return _config.ExactMatch ? _current == cost : _current >= cost;
    }

    /// <inheritdoc />
    public bool Consume(int amount)
    {
        if (!HasResource(amount)) return false;

        if (_config.ConsumeAll)
            _current = 0;
        else
            _current = (byte)Math.Max(0, _current - amount);

        return true;
    }

    /// <inheritdoc />
    public void Generate(int amount)
    {
        if (amount <= 0) return;

        int next = _current + amount;
        if (next > _config.Max)
            _current = _config.WrapOnOverflow ? (byte)1 : _config.Max;
        else
            _current = (byte)next;
    }

    /// <inheritdoc />
    public void NotifyAction(long tick)
    {
        _lastActionTick = tick;
    }

    /// <inheritdoc />
    public void SetResource(byte value)
    {
        _current = Math.Min(value, _config.Max);
    }

    /// <inheritdoc />
    public void Update(long tick)
    {
        if (_current == 0 || _config.TimeoutMs == 0) return;

        if (tick - _lastActionTick >= _config.TimeoutMs)
        {
            _current = 0;
        }
    }
}
