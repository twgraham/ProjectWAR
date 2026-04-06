using Core.GameWorld.Entities;

namespace Core.GameWorld.Combat.Buffs;

/// <summary>
/// Lifecycle callbacks for a single effect within a <see cref="Buff"/>.
/// <para>
/// Each <see cref="BuffEffectDefinition"/> in a <see cref="BuffDefinition"/> is
/// instantiated into an <c>IBuffEffect</c> when the buff is applied. Concrete
/// implementations (Step 6) include <c>StatModifierEffect</c>,
/// <c>DamageOverTimeEffect</c>, <c>AbsorbShieldEffect</c>, etc.
/// </para>
/// <para>
/// All callbacks are invoked on the region thread — no synchronization needed.
/// </para>
/// </summary>
public interface IBuffEffect
{
    /// <summary>The definition this effect was instantiated from.</summary>
    BuffEffectDefinition Definition { get; }

    /// <summary>
    /// Called once when the buff is first applied to the target.
    /// Stat modifiers add their bonus here; CC flags are applied here.
    /// </summary>
    void OnStart(Buff buff, UnitEntity target);

    /// <summary>
    /// Called on each tick interval. DoTs deal damage; HoTs heal; resource effects
    /// drain/grant resource. Not called if the buff has no tick interval.
    /// </summary>
    void OnTick(Buff buff, UnitEntity target, long tick);

    /// <summary>
    /// Called when the buff expires or is removed. Stat modifiers undo their bonus;
    /// CC flags are cleared.
    /// </summary>
    void OnEnd(Buff buff, UnitEntity target);

    /// <summary>
    /// Called when a subscribed combat event fires. Only invoked if the effect's
    /// <see cref="BuffEffectDefinition.EventSubscription"/> matches the event type.
    /// <para>
    /// The effect may mutate <paramref name="context"/> in place (e.g., absorb shields
    /// reduce damage, damage mods scale damage).
    /// </para>
    /// </summary>
    /// <param name="buff">The owning buff instance.</param>
    /// <param name="eventType">The combat event that fired.</param>
    /// <param name="context">Mutable damage context — may be null for non-damage events.</param>
    /// <param name="instigator">The entity that triggered the event (attacker/healer).</param>
    void OnCombatEvent(Buff buff, CombatEventType eventType, DamageContext? context, UnitEntity? instigator);
}
