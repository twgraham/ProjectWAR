using Core.Domain.Entities;
using Core.GameWorld.Combat;
using Core.GameWorld.Combat.Abilities;
using Core.GameWorld.Combat.AutoAttack;
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

/// <summary>
/// Fired when an ability with a positive <c>EffectDelay</c> launches a projectile.
/// Handlers send <c>F_USE_ABILITY</c> (state=6) to the caster and nearby players.
/// Damage is applied server-side after <see cref="FlightTimeMs"/> elapses.
/// </summary>
public readonly record struct AbilityProjectileFired(
    UnitEntity Caster,
    AbilityCastContext Context,
    ushort FlightTimeMs) : ITickEvent;

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

// ── Entity lifecycle ─────────────────────────────────────────────────

/// <summary>
/// Fired when a unit's health reaches zero. Emitted by the entity when
/// <see cref="Components.HealthComponent.TakeDamage"/> reduces HP to 0.
/// </summary>
public readonly record struct EntityDied(UnitEntity Entity) : ITickEvent;

// ── Auto-attack ──────────────────────────────────────────────────

/// <summary>
/// Fired when an auto-attack swing begins (before damage resolution).
/// <para>Handlers send <c>F_USE_ABILITY</c> with <c>abilityEntry = 0</c>
/// (state = completed) to trigger the melee/ranged swing animation on the client.</para>
/// </summary>
public readonly record struct AutoAttackSwing(
    UnitEntity Caster,
    UnitEntity Target) : ITickEvent;

/// <summary>
/// Fired when auto-attack damage is resolved (main-hand, offhand, or ranged).
/// <para>Handlers send <c>F_CAST_PLAYER_EFFECT</c> (0xB3) and <c>F_HIT_PLAYER</c>
/// (0x14) just like ability damage, but with <c>abilityEntry = 0</c>.</para>
/// </summary>
public readonly record struct AutoAttackDamageDealt(
    UnitEntity Caster,
    UnitEntity Target,
    DamageContext Context) : ITickEvent;

// ── Combat state ─────────────────────────────────────────────────

/// <summary>
/// Fired when a unit enters or leaves combat.
/// <para>Handlers send <c>F_UPDATE_STATE</c> with <c>StateOpcode = 0x1A</c>
/// (val1: 1 = enter, 0 = leave) to the entity and nearby players.</para>
/// </summary>
public readonly record struct CombatStateChanged(
    UnitEntity Entity,
    bool InCombat) : ITickEvent;