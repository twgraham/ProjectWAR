using Core.GameWorld.Combat;
using Core.GameWorld.Combat.Abilities;
using Core.GameWorld.Combat.AutoAttack;
using Core.GameWorld.Combat.Buffs;
using Core.GameWorld.Components;
using Core.GameWorld.Events;
using Core.GameWorld.Stats;

namespace Core.GameWorld.Entities;

/// <summary>
/// Abstract base for all combat-capable entities (players, creatures, pets).
/// Provides guaranteed <see cref="Health"/>, <see cref="Abilities"/>, <see cref="Level"/>,
/// <see cref="Realm"/>, and <see cref="Faction"/> as direct fields — no component lookup needed.
/// <para>
/// Combat systems can accept <c>UnitEntity</c> as a parameter type and be confident
/// that health, level, abilities, and realm are always available.
/// </para>
/// <para>
/// Component callbacks are wired at construction. Each component signals domain-level
/// facts (e.g. "cast completed", "died"); the entity translates them into region-level
/// <see cref="ITickEvent"/> instances via the protected <see cref="WorldEntity.Emit"/> method.
/// </para>
/// </summary>
public abstract class UnitEntity : WorldEntity
{
    /// <summary>
    /// Shared default <see cref="AbilityEffectExecutor"/> used by all entities unless
    /// overridden via <c>entity.Abilities.EffectExecutor = …</c>.
    /// <para>
    /// Wire <see cref="AbilityEffectExecutor.BuffLookup"/> on this instance at startup
    /// to enable buff invocation globally.
    /// </para>
    /// </summary>
    public static AbilityEffectExecutor SharedEffectExecutor { get; set; } = new();

    protected UnitEntity(ushort objectId, EntityType type, string name, uint maxHealth)
        : base(objectId, type, name)
    {
        Health = new HealthComponent(maxHealth);
        Abilities = new AbilityComponent(this) { EffectExecutor = SharedEffectExecutor };
        Stats = new StatContainer();
        Buffs = new BuffContainer(this);
        CombatState = new CombatStateTracker();

        // ── Stub delegates for auto-attack ──────────────────────────
        // TODO: Replace with real equipment queries once the item/equip system
        //       is wired. The stub returns a generic 50-DPS, 2.0s weapon.
        WeaponQuery stubWeapon = static (_, _) => new WeaponInfo(50f, 200);

        // TODO: Replace with WorldPosition.DistanceSquared2D or spatial query
        //       once movement / position updates are flowing.
        DistanceFunc stubDistance = static (a, b) =>
        {
            var dx = a.Position.X - b.Position.X;
            var dy = a.Position.Y - b.Position.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy) / 10;
        };

        // TODO: Wire real LOS via Core.Spatial raycasting when zone
        //       heightmap data is loaded.
        LosFunc stubLos = static (_, _) => true;

        // TODO: Implement facing-arc check using entity heading.
        FacingFunc stubFacing = static (_, _) => true;

        AutoAttack = new AutoAttackComponent(
            this,
            new AutoAttackConfig(),
            stubWeapon,
            stubDistance,
            stubLos,
            stubFacing,
            Random.Shared.Next);

        Stats.OnMaxHealthChanged = newMax => Health.Max = newMax;

        // ── Wire component callbacks → tick events ──────────────────
        Health.OnDied = () => Emit(new EntityDied(this));

        Abilities.OnCastConfirmed = ctx => Emit(new AbilityCastConfirmed(this, ctx));
        Abilities.OnCastCompleted = ctx => Emit(new AbilityCastCompleted(this, ctx));
        Abilities.OnCastFailed = (ctx, reason) => Emit(new AbilityCastFailed(this, ctx, reason));
        Abilities.OnCooldownApplied = (entry, ms) => Emit(new AbilityCooldownApplied(this, entry, ms));
        Abilities.OnDamageDealt = (caster, result) => Emit(new DamageDealt(
            caster, result.Target, result.AbilityEntry, result.CommandIndex,
            result.Damage, result.Mitigation, result.Absorption,
            result.WasCritical, result.WasDefended, result.DefenseType));

        // ── Auto-attack callbacks → tick events ─────────────────────
        AutoAttack.OnSwing = (caster, target) =>
            Emit(new AutoAttackSwing(caster, target));

        AutoAttack.OnDamageDealt = (caster, ctx) =>
        {
            // Target is the current auto-attack target at the time damage was dealt
            var target = AutoAttack.Target;
            if (target is not null)
                Emit(new AutoAttackDamageDealt(caster, target, ctx));
            CombatState.RefreshCombat(0); // tick will be corrected by next Update
        };

        // ── Combat state callbacks → tick events ────────────────────
        CombatState.OnCombatStateChanged = entered =>
            Emit(new CombatStateChanged(this, entered));
    }

    /// <summary>Health pool — always present on units. Never null.</summary>
    public HealthComponent Health { get; }

    /// <summary>Per-entity ability state (active cast, cooldowns, GCD) — always present on units. Never null.</summary>
    public AbilityComponent Abilities { get; }

    /// <summary>Stat modifier container — always present on units. Never null.</summary>
    public StatContainer Stats { get; }

    /// <summary>Buff container — always present on units. Never null.</summary>
    public BuffContainer Buffs { get; }

    /// <summary>
    /// Auto-attack state (timing, target, melee/ranged/offhand) — always present on units.
    /// For creatures, <see cref="AutoAttackComponent.IsAttacking"/> starts <c>false</c>
    /// and is activated by the AI system.
    /// </summary>
    public AutoAttackComponent AutoAttack { get; }

    /// <summary>
    /// Combat-state tracker (in/out of combat, 10s timeout) — always present on units.
    /// Transitions emit <see cref="CombatStateChanged"/> for <c>F_UPDATE_STATE</c> packets.
    /// </summary>
    public CombatStateTracker CombatState { get; }

    /// <summary>Unit level (1–40 for players, variable for creatures).</summary>
    public byte Level { get; set; }

    /// <summary>Faction affiliation (Order / Destruction / Neutral).</summary>
    public byte Realm { get; set; }

    /// <summary>Raw faction value used for aggression rules.</summary>
    public byte Faction { get; set; }

    /// <summary>Current action points. Consumed by ability casts, regenerated by tick systems.</summary>
    public int ActionPoints { get; set; }

    /// <summary>
    /// OID of the entity's current offensive target. Set by the client via
    /// <c>F_PLAYER_INFO</c> (target-update packet). <c>0</c> = no target selected.
    /// <para>
    /// Read advisorily by <see cref="Combat.Abilities.AbilityComponent"/> to resolve
    /// the target at cast initiation. The region thread may also read this during
    /// action execution.
    /// </para>
    /// </summary>
    public ushort? CurrentTargetOid { get; set; }

    /// <summary>
    /// Override to tick unit-specific state (HP regen, combat timers) before
    /// optional component ticks.
    /// </summary>
    public override void Update(long tick)
    {
        Buffs.Update(tick);
        Stats.Flush();
        Abilities.Update(tick);
        AutoAttack.Update(tick);
        CombatState.Update(tick);
        base.Update(tick);
    }
}
