namespace Core.GameWorld.Combat.Career;

/// <summary>
/// Configuration for <see cref="StanceResource"/> — a discrete mode selector
/// with no numeric bar.
/// <para>
/// Covers: Knight/Chosen (aura stances), Marauder (arm mutations),
/// Shadow Warrior (Scout/Assault/Skirmish), White Lion, RP/Zealot, Squig Herder.
/// </para>
/// </summary>
public sealed record StanceResourceConfig
{
    /// <summary>
    /// Number of valid stances (e.g. 3 for Marauder arms, 3 for SW stances).
    /// Stance values are 0 (none) through <c>StanceCount</c>.
    /// </summary>
    public byte StanceCount { get; init; } = 3;

    /// <summary>
    /// Optional composite stance masks for abilities that require "any of several stances".
    /// Key = cost value from ability definition, Value = set of valid stance IDs.
    /// <para>
    /// Example (Marauder): cost 4 → stances {1,2}, cost 5 → {2,3}, cost 6 → {1,3}, cost 7 → {1,2,3}.
    /// </para>
    /// </summary>
    public IReadOnlyDictionary<int, HashSet<byte>>? CompositeMasks { get; init; }

    /// <summary>
    /// Optional callback fired when stance changes.
    /// Used for visual effects, buff swaps, etc.
    /// </summary>
    public Action<ICareerResource, byte>? OnStanceChanged { get; init; }
}

/// <summary>
/// A discrete stance selector. <see cref="Current"/> is the active stance index.
/// <see cref="HasResource"/> checks if the caster is in the required stance (or a
/// composite match for multi-stance abilities).
/// </summary>
public sealed class StanceResource : ICareerResource
{
    private readonly StanceResourceConfig _config;

    private byte _current;

    public StanceResource(StanceResourceConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc />
    /// <remarks>Current stance index (0 = no stance).</remarks>
    public byte Current => _current;

    /// <inheritdoc />
    public byte Max => _config.StanceCount;

    /// <inheritdoc />
    /// <remarks>For stances, Level = Current stance.</remarks>
    public byte Level => _current;

    /// <inheritdoc />
    /// <remarks>
    /// For exact stance match: cost ≤ StanceCount → must be in that stance.
    /// For composite masks: cost > StanceCount → check the mask set.
    /// Cost 0 always passes (no stance requirement).
    /// </remarks>
    public bool HasResource(int cost)
    {
        if (cost == 0) return true;

        // Direct stance check
        if (cost <= _config.StanceCount)
            return _current == cost;

        // Composite mask check
        if (_config.CompositeMasks is not null
            && _config.CompositeMasks.TryGetValue(cost, out var validStances))
        {
            return validStances.Contains(_current);
        }

        return false;
    }

    /// <summary>Stances are not consumed. Always returns <c>true</c> if <see cref="HasResource"/> passes.</summary>
    public bool Consume(int amount) => HasResource(amount);

    /// <summary>Generate is a no-op for stances — use <see cref="SetResource"/> to switch.</summary>
    public void Generate(int amount) { }

    /// <inheritdoc />
    public void NotifyAction(long tick) { }

    /// <inheritdoc />
    public void SetResource(byte value)
    {
        if (value > _config.StanceCount) return;

        var old = _current;
        _current = value;
        if (old != _current)
            _config.OnStanceChanged?.Invoke(this, _current);
    }

    /// <summary>Stances don't tick.</summary>
    public void Update(long tick) { }
}
