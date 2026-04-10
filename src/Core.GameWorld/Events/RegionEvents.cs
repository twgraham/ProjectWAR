using Core.Domain.Entities;
using Core.GameWorld.Combat;
using Core.GameWorld.Combat.Abilities;
using Core.GameWorld.Entities;

namespace Core.GameWorld.Events;

// Visibility lifecycle
public readonly record struct EntityBecameVisible(PlayerEntity Observer, WorldEntity Entity, ZoneInfo Zone);
public readonly record struct EntityLeftVisibility(PlayerEntity Observer, WorldEntity Entity);
public readonly record struct EntityStateChanged(PlayerEntity Observer, WorldEntity Entity, ZoneInfo Zone);

// ── Ability lifecycle ────────────────────────────────────────────────

/// <summary>
/// Fired on the region thread when a cast is confirmed (instant casts have already
/// completed by the time this fires; cast-bar abilities have just started).
/// <para>Handlers send <c>F_USE_ABILITY</c> (state=completed or state=start depending
/// on CastState) to nearby players and — for cast-bar abilities — <c>F_SET_ABILITY_TIMER</c>
/// to the caster.</para>
/// </summary>
public readonly record struct AbilityCastConfirmed(
    UnitEntity Caster,
    AbilityCastContext Context) : ITickEvent;

/// <summary>
/// Fired when a cast-bar or channeled ability completes execution on the region thread.
/// Handlers send <c>F_USE_ABILITY</c> (state=completed) and cooldown timer packets.
/// </summary>
public readonly record struct AbilityCastCompleted(
    UnitEntity Caster,
    AbilityCastContext Context) : ITickEvent;

/// <summary>
/// Fired when a cast is cancelled or fails re-validation on the region thread.
/// Handlers send <c>F_USE_ABILITY</c> (state=cancelled) with the failure code.
/// </summary>
public readonly record struct AbilityCastFailed(
    UnitEntity Caster,
    AbilityCastContext Context,
    AbilityFailure Reason) : ITickEvent;

/// <summary>
/// Fired when a cooldown is applied after cast completion.
/// Handlers send <c>F_SET_ABILITY_TIMER</c> (cooldown format) to the caster.
/// </summary>
public readonly record struct AbilityCooldownApplied(
    UnitEntity Caster,
    ushort AbilityEntry,
    int CooldownMs) : ITickEvent;

// ── Combat damage ────────────────────────────────────────────────────

/// <summary>
/// Fired when an ability (or channel tick) deals damage to a target.
/// <para>Handlers build and broadcast <c>F_CAST_PLAYER_EFFECT</c> (0xB3) to
/// the target, caster, and nearby players with damage numbers, mitigation,
/// absorption, critical/defense indicators.</para>
/// </summary>
public readonly record struct DamageDealt(
    UnitEntity Caster,
    UnitEntity Target,
    ushort AbilityEntry,
    byte CommandIndex,
    uint Damage,
    uint Mitigation,
    uint Absorption,
    bool WasCritical,
    bool WasDefended,
    DefenseType DefenseType) : ITickEvent;