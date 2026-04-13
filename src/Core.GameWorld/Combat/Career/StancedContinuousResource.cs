namespace Core.GameWorld.Combat.Career;

/// <summary>
/// Configuration for <see cref="StancedContinuousResource"/> — a continuous bar
/// whose generation/drain rate depends on the active stance.
/// <para>
/// Covers: Warrior Priest (Righteous Fury) and Disciple of Khaine (Soul Essence).
/// </para>
/// </summary>
public sealed record StancedContinuousConfig
{
    /// <summary>Maximum resource value (e.g. 250 for WP/DoK).</summary>
    public byte Max { get; init; } = 250;

    /// <summary>Starting resource value (WP/DoK start full).</summary>
    public byte InitialValue { get; init; } = 250;

    /// <summary>Number of distinct stances (e.g. 3: DPS/Drain, Devotion, Absolution).</summary>
    public byte StanceCount { get; init; } = 3;

    /// <summary>
    /// Per-tick regeneration rate when out of combat, in resource per second.
    /// </summary>
    public byte OutOfCombatRegenPerSec { get; init; } = 20;

    /// <summary>
    /// Per-stance in-combat drain rate (resource per second). Index = stance - 1.
    /// Positive = drain, negative = generate.
    /// Entry for stance 0 (none) is index 0 with default 0.
    /// Example: WP stance 1 drains 5/s in combat.
    /// </summary>
    public sbyte[] InCombatDrainPerStance { get; init; } = [0, 5, 0, 0];

    /// <summary>
    /// Tick interval for regen/drain (ms).
    /// </summary>
    public ushort TickIntervalMs { get; init; } = 1000;

    /// <summary>
    /// Conversion factor from resource to level (e.g. 0.16 for WP → level = resource × 0.16).
    /// </summary>
    public float LevelConversionFactor { get; init; } = 0.16f;

    /// <summary>Optional callback when level changes.</summary>
    public Action<ICareerResource, byte>? OnLevelChanged { get; init; }

    /// <summary>Optional callback when stance changes.</summary>
    public Action<ICareerResource, byte>? OnStanceChanged { get; init; }
}

/// <summary>
/// A continuous resource bar with stance-dependent generation/drain rates.
/// Active stance affects how quickly the bar fills or empties.
/// </summary>
public sealed class StancedContinuousResource : ICareerResource
{
    private readonly StancedContinuousConfig _config;

    private byte _current;
    private byte _stance;
    private byte _level;
    private bool _inCombat;
    private long _lastTickTime;

    public StancedContinuousResource(StancedContinuousConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _current = Math.Min(config.InitialValue, config.Max);
        RecalcLevel();
    }

    /// <inheritdoc />
    public byte Current => _current;

    /// <inheritdoc />
    public byte Max => _config.Max;

    /// <inheritdoc />
    public byte Level => _level;

    /// <summary>Current active stance (0 = none).</summary>
    public byte Stance => _stance;

    /// <summary>
    /// Set the in-combat flag. Affects whether regen or drain applies.
    /// </summary>
    public bool InCombat
    {
        get => _inCombat;
        set => _inCombat = value;
    }

    /// <inheritdoc />
    public bool HasResource(int cost) => _current >= cost;

    /// <inheritdoc />
    public bool Consume(int amount)
    {
        if (_current < amount) return false;
        _current = (byte)Math.Max(0, _current - amount);
        RecalcLevel();
        return true;
    }

    /// <inheritdoc />
    public void Generate(int amount)
    {
        if (amount <= 0) return;
        _current = (byte)Math.Min(_config.Max, _current + amount);
        RecalcLevel();
    }

    /// <inheritdoc />
    public void NotifyAction(long tick) { }

    /// <inheritdoc />
    /// <remarks>
    /// SetResource with values > StanceCount sets the resource value.
    /// Values 1–StanceCount switch the active stance.
    /// </remarks>
    public void SetResource(byte value)
    {
        if (value >= 1 && value <= _config.StanceCount)
        {
            SetStance(value);
        }
        else
        {
            _current = Math.Min(value, _config.Max);
            RecalcLevel();
        }
    }

    /// <summary>Switch to a different stance.</summary>
    public void SetStance(byte stance)
    {
        if (stance > _config.StanceCount) return;

        var old = _stance;
        _stance = stance;
        if (old != _stance)
            _config.OnStanceChanged?.Invoke(this, _stance);
    }

    /// <inheritdoc />
    public void Update(long tick)
    {
        if (_config.TickIntervalMs == 0) return;
        if (tick - _lastTickTime < _config.TickIntervalMs) return;

        _lastTickTime = tick;

        if (_inCombat)
        {
            // Drain based on current stance
            int stanceIndex = _stance;
            if (stanceIndex >= 0 && stanceIndex < _config.InCombatDrainPerStance.Length)
            {
                int drain = _config.InCombatDrainPerStance[stanceIndex];
                if (drain > 0)
                    _current = (byte)Math.Max(0, _current - drain);
                else if (drain < 0)
                    _current = (byte)Math.Min(_config.Max, _current - drain); // negative drain = generate
            }
        }
        else
        {
            // Out of combat: regen
            if (_config.OutOfCombatRegenPerSec > 0)
                _current = (byte)Math.Min(_config.Max, _current + _config.OutOfCombatRegenPerSec);
        }

        RecalcLevel();
    }

    // ── Internals ────────────────────────────────────────────────────

    private void RecalcLevel()
    {
        byte newLevel = (byte)Math.Min(255, (int)(_current * _config.LevelConversionFactor));
        if (newLevel != _level)
        {
            _level = newLevel;
            _config.OnLevelChanged?.Invoke(this, _level);
        }
    }
}
