using WorldServerV2.World.Stats;

namespace WorldServerV2.World.Combat.Career;

/// <summary>
/// Configuration for <see cref="ContinuousResource"/> — a numeric 0–Max bar that
/// generates by actions and decays over time.
/// <para>
/// Covers: Ironbreaker (Oath), Blackguard (Hatred), BW/Sorc (Combustion/Dark Magic),
/// Slayer/Choppa (Rage).
/// </para>
/// </summary>
public sealed record ContinuousResourceConfig
{
    /// <summary>Maximum resource value (e.g. 100 for most careers).</summary>
    public byte Max { get; init; } = 100;

    /// <summary>Amount drained per decay tick.</summary>
    public byte DecayRate { get; init; } = 20;

    /// <summary>Interval between decay ticks, in ms.</summary>
    public ushort DecayIntervalMs { get; init; } = 2000;

    /// <summary>
    /// Duration of inactivity (ms) before decay begins.
    /// Set to 0 to decay immediately when out of combat.
    /// </summary>
    public ushort IdleTimeoutMs { get; init; } = 10_000;

    /// <summary>
    /// Level breakpoints: level = number of thresholds ≤ Current.
    /// Example: [25, 50, 75, 100] → 4 levels for an IB oath bar.
    /// </summary>
    public byte[] LevelThresholds { get; init; } = [25, 50, 75, 100];

    /// <summary>
    /// Optional stat bonuses applied at each level. Index = level (1-based).
    /// Each entry is a list of (StatId, value) pairs.
    /// </summary>
    public (StatId Stat, int Value)[][]? StatBonusesPerLevel { get; init; }

    /// <summary>
    /// Optional callback fired when the derived level changes.
    /// Used for career-specific quirks (BW backlash, Slayer state switch).
    /// </summary>
    public Action<ICareerResource, byte>? OnLevelChanged { get; init; }
}

/// <summary>
/// A numeric resource bar (0–Max) that generates via <see cref="ICareerResource.Generate"/>
/// and decays back to zero after an inactivity timeout.
/// <para>
/// Level is computed by counting how many <see cref="ContinuousResourceConfig.LevelThresholds"/>
/// are ≤ <see cref="Current"/>. Stat bonuses per level are applied/removed on level transitions.
/// </para>
/// </summary>
public sealed class ContinuousResource : ICareerResource
{
    private readonly ContinuousResourceConfig _config;

    private byte _current;
    private byte _level;
    private long _lastActionTick;
    private long _lastDecayTick;

    public ContinuousResource(ContinuousResourceConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc />
    public byte Current => _current;

    /// <inheritdoc />
    public byte Max => _config.Max;

    /// <inheritdoc />
    public byte Level => _level;

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
    public void NotifyAction(long tick)
    {
        _lastActionTick = tick;
    }

    /// <inheritdoc />
    public void SetResource(byte value)
    {
        _current = Math.Min(value, _config.Max);
        RecalcLevel();
    }

    /// <inheritdoc />
    public void Update(long tick)
    {
        if (_current == 0) return;

        // Don't decay until idle timeout has passed since last action
        if (_config.IdleTimeoutMs > 0 && tick - _lastActionTick < _config.IdleTimeoutMs)
            return;

        // Decay at intervals
        if (_config.DecayIntervalMs > 0 && tick - _lastDecayTick < _config.DecayIntervalMs)
            return;

        _lastDecayTick = tick;
        _current = (byte)Math.Max(0, _current - _config.DecayRate);
        RecalcLevel();
    }

    // ── Internals ────────────────────────────────────────────────────

    private void RecalcLevel()
    {
        byte newLevel = 0;
        var thresholds = _config.LevelThresholds;
        for (int i = 0; i < thresholds.Length; i++)
        {
            if (_current >= thresholds[i])
                newLevel = (byte)(i + 1);
        }

        if (newLevel != _level)
        {
            _level = newLevel;
            _config.OnLevelChanged?.Invoke(this, _level);
        }
    }
}
