using Core.GameWorld.Entities;

namespace Core.GameWorld.Combat;

/// <summary>
/// Tracks in-combat / out-of-combat state for a <see cref="UnitEntity"/>.
/// <para>
/// Entering combat is instantaneous — any hostile interaction calls <see cref="RefreshCombat"/>.
/// Leaving combat requires <see cref="CombatTimeoutMs"/> (10 seconds by default) of inactivity.
/// </para>
/// <para>
/// Owned directly by <see cref="UnitEntity"/> as a guaranteed field.
/// Ticked explicitly from <see cref="UnitEntity.Update"/>.
/// </para>
/// </summary>
public sealed class CombatStateTracker
{
    /// <summary>Default timeout before leaving combat (V1 default: 10 000 ms).</summary>
    public const long DefaultCombatTimeoutMs = 10_000;

    private readonly long _timeoutMs;
    private long _combatExpireTime;

    /// <summary>Whether the unit is currently in combat.</summary>
    public bool IsInCombat { get; private set; }

    /// <summary>
    /// Callback invoked when combat state transitions (<c>true</c> = entered combat,
    /// <c>false</c> = left combat). The entity wires this to emit a
    /// <see cref="Events.CombatStateChanged"/> tick event.
    /// </summary>
    public Action<bool>? OnCombatStateChanged { get; set; }

    /// <summary>
    /// Creates a new combat-state tracker.
    /// </summary>
    /// <param name="timeoutMs">
    /// Time in milliseconds of inactivity before the unit leaves combat.
    /// Defaults to <see cref="DefaultCombatTimeoutMs"/> (10 000 ms).
    /// </param>
    public CombatStateTracker(long timeoutMs = DefaultCombatTimeoutMs)
    {
        _timeoutMs = timeoutMs;
    }

    /// <summary>
    /// Refreshes (or enters) combat. The expiry timer is reset to
    /// <c>tick + timeout</c> on every call.
    /// <para>
    /// Call this whenever a hostile interaction occurs — auto-attack swing,
    /// ability damage dealt, ability damage received, debuff applied, etc.
    /// </para>
    /// </summary>
    public void RefreshCombat(long tick)
    {
        _combatExpireTime = tick + _timeoutMs;

        if (IsInCombat)
            return;

        IsInCombat = true;
        OnCombatStateChanged?.Invoke(true);
    }

    /// <summary>
    /// Ticked from <see cref="UnitEntity.Update"/>. Checks whether the combat
    /// timeout has elapsed and transitions out of combat if so.
    /// </summary>
    public void Update(long tick)
    {
        if (!IsInCombat)
            return;

        if (tick < _combatExpireTime)
            return;

        IsInCombat = false;
        OnCombatStateChanged?.Invoke(false);
    }

    /// <summary>
    /// Forces the unit out of combat immediately (e.g. on death or zone transition).
    /// </summary>
    public void ForceLeave()
    {
        if (!IsInCombat)
            return;

        IsInCombat = false;
        OnCombatStateChanged?.Invoke(false);
    }
}
