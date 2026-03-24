namespace WorldServerV2.World.Combat.Career;

/// <summary>
/// Configuration for <see cref="BalanceNeedleResource"/> — a bidirectional bar
/// pushed by damage vs heal casts.
/// <para>
/// Covers: Archmage (Force/Tranquility), Shaman (Waaagh/Mork).
/// Damage abilities push toward one extreme; heal abilities push toward the other.
/// Bonuses increase at the extremes.
/// </para>
/// </summary>
public sealed record BalanceNeedleConfig
{
    /// <summary>
    /// Maximum value on each side. Total range is 1 to <c>Max × 2</c>, with center at <c>Max</c>.
    /// Default 5 → internal values 1..10, center = 5.
    /// <para>
    /// Client display: 1–5 = damage side (Force), 6–10 = heal side (Tranquility).
    /// </para>
    /// </summary>
    public byte Max { get; init; } = 5;

    /// <summary>
    /// Inactivity timeout (ms) before the needle decays one step toward center.
    /// </summary>
    public ushort IdleTimeoutMs { get; init; } = 15_000;

    /// <summary>
    /// Interval between decay steps (ms) once idle timeout has elapsed.
    /// </summary>
    public ushort DecayIntervalMs { get; init; } = 2000;

    /// <summary>
    /// Optional callback when level changes (for buff visual updates, stat bonuses).
    /// </summary>
    public Action<ICareerResource, byte>? OnLevelChanged { get; init; }
}

/// <summary>
/// Bidirectional balance needle (Archmage/Shaman). 
/// <para>
/// Internal values: 1 = full damage-side, <c>Max</c> = center, <c>Max × 2</c> = full heal-side.
/// <see cref="Generate"/> pushes toward damage-side (lower values).
/// <see cref="Consume"/> pushes toward heal-side (higher values).
/// <see cref="HasResource"/> always passes — the needle never blocks casts.
/// </para>
/// </summary>
public sealed class BalanceNeedleResource : ICareerResource
{
    private readonly BalanceNeedleConfig _config;

    private byte _current;
    private byte _level;
    private long _lastActionTick;
    private long _lastDecayTick;

    /// <summary>
    /// Creates a new balance needle, initialized to center.
    /// </summary>
    public BalanceNeedleResource(BalanceNeedleConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _current = config.Max; // start at center
    }

    /// <inheritdoc />
    /// <remarks>Internal position: 1..(Max×2). Center = Max.</remarks>
    public byte Current => _current;

    /// <inheritdoc />
    public byte Max => (byte)(_config.Max * 2);

    /// <summary>Center value of the needle.</summary>
    public byte Center => _config.Max;

    /// <inheritdoc />
    /// <remarks>
    /// Level = distance from center. 0 = balanced, positive = pushed to either extreme.
    /// </remarks>
    public byte Level => _level;

    /// <summary>
    /// Returns the number of steps toward the damage side (Current &lt; Center).
    /// 0 if at center or heal-side.
    /// </summary>
    public byte DamageSideDepth => _current < _config.Max ? (byte)(_config.Max - _current) : (byte)0;

    /// <summary>
    /// Returns the number of steps toward the heal side (Current &gt; Center).
    /// 0 if at center or damage-side.
    /// </summary>
    public byte HealSideDepth => _current > _config.Max ? (byte)(_current - _config.Max) : (byte)0;

    /// <inheritdoc />
    /// <remarks>Balance needle never blocks casts.</remarks>
    public bool HasResource(int cost) => true;

    /// <summary>
    /// Push toward heal-side (increment). Used by heal abilities.
    /// <paramref name="amount"/> is ignored — always moves 1 step.
    /// </summary>
    public bool Consume(int amount)
    {
        byte max = (byte)(_config.Max * 2);
        if (_current < max)
        {
            _current++;
            RecalcLevel();
        }
        return true;
    }

    /// <summary>
    /// Push toward damage-side (decrement). Used by damage abilities.
    /// <paramref name="amount"/> is ignored — always moves 1 step.
    /// </summary>
    public void Generate(int amount)
    {
        if (_current > 1)
        {
            _current--;
            RecalcLevel();
        }
    }

    /// <inheritdoc />
    public void NotifyAction(long tick)
    {
        _lastActionTick = tick;
    }

    /// <inheritdoc />
    public void SetResource(byte value)
    {
        byte max = (byte)(_config.Max * 2);
        _current = Math.Clamp(value, (byte)1, max);
        RecalcLevel();
    }

    /// <inheritdoc />
    /// <remarks>Decays one step toward center after idle timeout.</remarks>
    public void Update(long tick)
    {
        if (_current == _config.Max) return; // already centered

        if (_config.IdleTimeoutMs > 0 && tick - _lastActionTick < _config.IdleTimeoutMs)
            return;

        if (_config.DecayIntervalMs > 0 && tick - _lastDecayTick < _config.DecayIntervalMs)
            return;

        _lastDecayTick = tick;

        // Move one step toward center
        if (_current < _config.Max)
            _current++;
        else
            _current--;

        RecalcLevel();
    }

    // ── Internals ────────────────────────────────────────────────────

    private void RecalcLevel()
    {
        byte newLevel = (byte)Math.Abs(_current - _config.Max);
        if (newLevel != _level)
        {
            _level = newLevel;
            _config.OnLevelChanged?.Invoke(this, _level);
        }
    }
}
