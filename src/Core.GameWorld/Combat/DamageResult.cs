using Core.GameWorld.Entities;

namespace Core.GameWorld.Combat;

/// <summary>
/// Domain-level result of a damage effect execution. Carries all the information
/// needed to describe what happened without coupling to the event infrastructure.
/// <para>
/// Produced by <see cref="Abilities.AbilityEffectExecutor"/> and surfaced through
/// <see cref="Abilities.AbilityComponent.OnDamageDealt"/>. The owning entity
/// translates this into the region-level <see cref="Events.DamageDealt"/> tick event.
/// </para>
/// </summary>
public readonly record struct DamageResult(
    UnitEntity Target,
    ushort AbilityEntry,
    byte CommandIndex,
    uint Damage,
    uint Mitigation,
    uint Absorption,
    bool WasCritical,
    bool WasDefended,
    DefenseType DefenseType);
