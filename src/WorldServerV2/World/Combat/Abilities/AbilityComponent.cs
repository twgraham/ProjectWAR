using WorldServerV2.World.Components;

namespace WorldServerV2.World.Combat.Abilities;

/// <summary>
/// Per-entity ability state: tracks the active cast, cooldowns, and global cooldown.
/// Attached as an optional component via <see cref="Entities.WorldEntity.Attach{T}"/>.
/// <para>
/// This is pure state — <see cref="AbilityCastService"/> drives all transitions
/// and ticking. Separation of state and behavior supports testability and the
/// split initiation / execution thread model (§11.6).
/// </para>
/// </summary>
public sealed class AbilityComponent : ComponentBase
{
    private readonly Dictionary<ushort, long> _cooldowns = new();
    private long _globalCooldownExpiry;

    /// <summary>Default global cooldown duration in milliseconds.</summary>
    public const int DefaultGcdMs = 1500;

    // ═══════════════════════════════════════════════════════════════════
    //  ACTIVE CAST STATE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>The in-progress cast, or null if idle.</summary>
    public AbilityCastContext? ActiveCast { get; internal set; }

    /// <summary>True if a cast is in progress.</summary>
    public bool HasActiveCast => ActiveCast is not null;

    /// <summary>Next channel tick timestamp. Valid only when channeling.</summary>
    internal long NextChannelTick { get; set; }

    /// <summary>Whether the 60% range re-check has fired for the current cast.</summary>
    internal bool RangeCheckDone { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    //  GLOBAL COOLDOWN
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>True if the GCD has not yet expired.</summary>
    public bool IsOnGlobalCooldown(long tick) => tick < _globalCooldownExpiry;

    /// <summary>Start (or overwrite) the GCD from the current tick.</summary>
    public void SetGlobalCooldown(long tick, int durationMs = DefaultGcdMs)
    {
        _globalCooldownExpiry = tick + durationMs;
    }

    /// <summary>Immediately clear the GCD (used when a cast-bar ability is interrupted).</summary>
    public void ClearGlobalCooldown() => _globalCooldownExpiry = 0;

    // ═══════════════════════════════════════════════════════════════════
    //  PER-ABILITY COOLDOWNS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>True if the entry's cooldown has not yet expired.</summary>
    public bool IsOnCooldown(ushort entry, long tick)
    {
        return _cooldowns.TryGetValue(entry, out var expiry) && tick < expiry;
    }

    /// <summary>Start a cooldown for the given entry.</summary>
    public void SetCooldown(ushort entry, long tick, int durationMs)
    {
        _cooldowns[entry] = tick + durationMs;
    }

    /// <summary>Returns the expiry tick for an entry, or 0 if none set.</summary>
    public long GetCooldownExpiry(ushort entry)
    {
        return _cooldowns.TryGetValue(entry, out var expiry) ? expiry : 0;
    }

    /// <summary>Remove all expired cooldowns to free dictionary memory.</summary>
    public void PurgeExpired(long tick)
    {
        List<ushort>? expired = null;
        foreach (var (entry, expiry) in _cooldowns)
        {
            if (tick >= expiry)
            {
                expired ??= [];
                expired.Add(entry);
            }
        }

        if (expired is not null)
            foreach (var entry in expired)
                _cooldowns.Remove(entry);
    }

    /// <summary>Clears active cast state. Called by the service on completion or cancel.</summary>
    internal void ClearCast()
    {
        ActiveCast = null;
        NextChannelTick = 0;
        RangeCheckDone = false;
    }
}
