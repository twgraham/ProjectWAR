using Core.GameWorld.Entities;
using Core.GameWorld.Spatial;
using Core.GameWorld.Stats;

namespace Core.GameWorld.Combat.AutoAttack;

/// <summary>
/// Configuration for auto-attack behaviour. Immutable once created.
/// </summary>
/// <param name="MeleeRange">Melee range in game-units (V1 default: 5).</param>
/// <param name="BaseRangedRange">Base ranged range before stat bonus (V1: 90).</param>
/// <param name="DefaultWeaponSpeed">Fallback weapon speed in tenths-of-seconds when no weapon (V1: 200 = 2.0s).</param>
/// <param name="OffhandBaseChance">Base offhand proc chance in percent (V1: 45).</param>
/// <param name="OffhandDamagePenalty">Offhand damage multiplier (V1: 0.9 = 90%).</param>
/// <param name="AutoAttackStatCoefficient">Stat coefficient for auto-attacks (V1: 0.1).</param>
/// <param name="OffhandStatCoefficient">Stat coefficient for offhand swings (V1: 0.05).</param>
/// <param name="RetryIntervalMs">Back-off delay when attack conditions fail (V1: 100ms).</param>
/// <param name="RangedLosRetryMs">Extra delay when LOS check fails for ranged (V1: 1000ms).</param>
/// <param name="DamageVariance">Weapon damage variance ±N% (V1: 25 = ±25%).</param>
public sealed record AutoAttackConfig(
    uint MeleeRange = 5,
    uint BaseRangedRange = 90,
    ushort DefaultWeaponSpeed = 200,
    int OffhandBaseChance = 45,
    float OffhandDamagePenalty = 0.9f,
    float AutoAttackStatCoefficient = 0.1f,
    float OffhandStatCoefficient = 0.05f,
    long RetryIntervalMs = 100,
    long RangedLosRetryMs = 1000,
    ushort DamageVariance = 25);

/// <summary>
/// Slot identity for weapon lookup.
/// </summary>
public enum WeaponSlot : byte
{
    MainHand = 0,
    OffHand = 1,
    Ranged = 2,
}

/// <summary>
/// Weapon stats resolved from the equipment system.
/// </summary>
public sealed record WeaponInfo(
    float Dps,
    ushort Speed,
    bool IsTwoHanded = false,
    bool IsShield = false,
    bool IsCharm = false);

/// <summary>
/// Callback invoked when auto-attack deals damage (main-hand, offhand, ranged).
/// Useful for career resource generation, threat management, etc.
/// </summary>
public delegate void OnAutoAttackHit(UnitEntity attacker, UnitEntity target, DamageContext ctx);

/// <summary>
/// Controls auto-attack timing, range checks, and offhand procs for a <see cref="UnitEntity"/>.
/// <para>
/// Owned directly by <see cref="UnitEntity"/> as a guaranteed field (like Health, Stats,
/// Abilities). Ticked explicitly from <see cref="UnitEntity.Update"/> rather than through
/// the optional component bag.
/// </para>
/// <para>
/// Each tick checks: alive → CC → casting → active → timing → range (melee or ranged)
/// → swing → offhand proc → schedule next attack.
/// </para>
/// <para>
/// <b>Ability interaction (matches V1):</b> Auto-attacks are blocked while
/// <see cref="UnitEntity.Abilities"/> has an active cast (cast-bar or channel).
/// Instant casts complete within a single tick and do not block. When blocked,
/// the scheduled swing remains due and fires on the update that the cast completes.
/// </para>
/// </summary>
public sealed class AutoAttackComponent
{
    private readonly UnitEntity _owner;
    private readonly AutoAttackConfig _config;
    private readonly Func<int, int, int> _random;

    private long _nextAttackTime;

    /// <summary>Whether auto-attack is currently enabled.</summary>
    public bool IsAttacking { get; set; }

    /// <summary>Current auto-attack target.</summary>
    public UnitEntity? Target { get; set; }

    /// <summary>Optional callback when a swing lands (career resource, threat, etc.).</summary>
    public OnAutoAttackHit? OnHit { get; set; }

    /// <summary>
    /// Callback invoked after each swing resolves damage. The entity wires this to
    /// emit <see cref="Events.DamageDealt"/> so the region event system can send packets.
    /// </summary>
    public Action<UnitEntity, DamageContext>? OnDamageDealt { get; set; }

    /// <summary>
    /// Callback invoked when a melee/ranged swing animation should play (before damage).
    /// The entity wires this to emit <see cref="Events.AutoAttackSwing"/> so the region
    /// handler can send <c>F_USE_ABILITY</c> with abilityId 0.
    /// </summary>
    public Action<UnitEntity, UnitEntity>? OnSwing { get; set; }

    /// <summary>True if the unit is currently moving (blocks ranged unless overridden).</summary>
    public bool IsMoving { get; set; }

    /// <summary>Whether this unit can shoot on the move (SW scout stance, etc.).</summary>
    public bool MoveAndShoot { get; set; }

    /// <summary>
    /// Creates a new auto-attack component owned by the given entity.
    /// </summary>
    /// <param name="owner">The unit that owns this auto-attack state.</param>
    /// <param name="config">Tuning parameters.</param>
    /// <param name="random">RNG returning int in [min, max) — for offhand proc and rolls.</param>
    public AutoAttackComponent(
        UnitEntity owner,
        AutoAttackConfig config,
        Func<int, int, int> random)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _config = config;
        _random = random;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  TICK
    // ═══════════════════════════════════════════════════════════════════

    public void Update(long tick)
    {
        var caster = _owner;

        // 1. Dead → stop
        if (caster.Health.IsDead)
        {
            IsAttacking = false;
            return;
        }

        // 2. CC check — disarmed/knocked/staggered
        if ((caster.Buffs.GetActiveCrowdControl() & CrowdControlFlags.NoAutoAttack) != 0)
            return;

        // 3. Casting/channelling check — matches V1 behaviour where IsCasting()
        //    blocks auto-attacks. Instant casts clear ActiveCast within the same
        //    tick they start, so they don't block. Cast-bar and channel abilities
        //    keep ActiveCast non-null for their full duration.
        if (caster.Abilities.HasActiveCast)
            return;

        // 4. Must be actively attacking with a target
        if (!IsAttacking || Target is null)
            return;

        // 5. Timing — not yet
        if (_nextAttackTime > tick)
            return;

        var target = Target;

        // 5. Basic target validation
        if (target.Health.IsDead)
        {
            IsAttacking = false;
            Target = null;
            return;
        }

        // 6. Check melee range first (edge-to-edge distance in feet)
        float distance = caster.DistanceTo(target);
        if (distance <= _config.MeleeRange)
        {
            // 6a. Facing check — skip swing if target not in front arc
            if (!caster.IsInFrontArc(target))
            {
                _nextAttackTime = tick + _config.RetryIntervalMs;
                return;
            }

            PerformMeleeSwing(caster, target, tick);
            return;
        }

        // 7. Ranged fallback
        PerformRangedSwing(caster, target, distance, tick);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MELEE SWING
    // ═══════════════════════════════════════════════════════════════════

    private void PerformMeleeSwing(UnitEntity caster, UnitEntity target, long tick)
    {
        var weapon = caster.GetWeaponInfo(WeaponSlot.MainHand);
        var weaponDps = weapon?.Dps ?? 0f;
        var weaponSpeed = weapon?.Speed ?? _config.DefaultWeaponSpeed;

        // Swing animation (F_USE_ABILITY with abilityId 0)
        OnSwing?.Invoke(caster, target);

        // Build + resolve damage
        var ctx = BuildAutoAttackContext(caster, target, weaponDps, weaponSpeed,
            StatId.Strength, DamageType.Physical, _config.AutoAttackStatCoefficient);

        DamagePipeline.Resolve(ctx);
        ApplyDamage(target, ctx);
        OnDamageDealt?.Invoke(caster, ctx);
        OnHit?.Invoke(caster, target, ctx);

        // Schedule next attack
        _nextAttackTime = tick + ComputeAttackInterval(caster, weaponSpeed);

        // Offhand proc
        TryOffhandSwing(caster, target);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RANGED SWING
    // ═══════════════════════════════════════════════════════════════════

    private void PerformRangedSwing(UnitEntity caster, UnitEntity target, float distance, long tick)
    {
        // Movement blocks ranged (unless MoveAndShoot)
        if (IsMoving && !MoveAndShoot)
        {
            _nextAttackTime = tick + _config.RetryIntervalMs;
            return;
        }

        // Must have ranged weapon
        var weapon = caster.GetWeaponInfo(WeaponSlot.Ranged);
        if (weapon is null)
        {
            _nextAttackTime = tick + _config.RetryIntervalMs;
            return;
        }

        // Range check (base + bonus stat)
        int rangeBonus = caster.Stats.GetTotal(StatId.Range);
        uint effectiveRange = (uint)(_config.BaseRangedRange + rangeBonus);
        if (distance > effectiveRange)
        {
            _nextAttackTime = tick + _config.RetryIntervalMs;
            return;
        }

        // LOS check (uses region occlusion provider; clear when unavailable)
        if (!caster.HasLineOfSight(target))
        {
            _nextAttackTime = tick + _config.RangedLosRetryMs;
            return;
        }

        // Swing animation
        OnSwing?.Invoke(caster, target);

        // Build + resolve
        var ctx = BuildAutoAttackContext(caster, target, weapon.Dps, weapon.Speed,
            StatId.BallisticSkill, DamageType.Physical, _config.AutoAttackStatCoefficient);

        DamagePipeline.Resolve(ctx);
        ApplyDamage(target, ctx);
        OnDamageDealt?.Invoke(caster, ctx);
        OnHit?.Invoke(caster, target, ctx);

        // Schedule next attack
        _nextAttackTime = tick + ComputeAttackInterval(caster, weapon.Speed);

        // No offhand proc for ranged
    }

    // ═══════════════════════════════════════════════════════════════════
    //  OFFHAND PROC
    // ═══════════════════════════════════════════════════════════════════

    private void TryOffhandSwing(UnitEntity caster, UnitEntity target)
    {
        var offhand = caster.GetWeaponInfo(WeaponSlot.OffHand);
        if (offhand is null || offhand.IsShield || offhand.IsCharm)
            return;

        int bonusChance = caster.Stats.GetTotal(StatId.OffhandProcChance);
        int roll = _random(1, 101); // [1..100]
        if (roll > _config.OffhandBaseChance + bonusChance)
            return;

        // Offhand swing uses main-hand speed for scaling (matches V1)
        var mainWeapon = caster.GetWeaponInfo(WeaponSlot.MainHand);
        var mhSpeed = mainWeapon?.Speed ?? _config.DefaultWeaponSpeed;

        var ctx = BuildAutoAttackContext(caster, target, offhand.Dps, mhSpeed,
            StatId.Strength, DamageType.Physical, _config.OffhandStatCoefficient);
        ctx.IsOffhand = true;

        DamagePipeline.Resolve(ctx);
        ApplyDamage(target, ctx);
        OnDamageDealt?.Invoke(caster, ctx);
        OnHit?.Invoke(caster, target, ctx);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DAMAGE CONTEXT FACTORY
    // ═══════════════════════════════════════════════════════════════════

    private DamageContext BuildAutoAttackContext(
        UnitEntity attacker,
        UnitEntity target,
        float weaponDps,
        ushort weaponSpeed,
        StatId primaryStat,
        DamageType damageType,
        float statCoefficient)
    {
        var ctx = new DamageContext
        {
            IsAutoAttack = true,
            DamageType = damageType,

            AttackerLevel = attacker.Level,
            TargetLevel = target.Level,

            // Weapon contribution
            WeaponDps = weaponDps,
            CastTimeDamageMult = weaponSpeed / 100f,
            StatCoefficient = statCoefficient,
            StatDamageScale = 1f,

            // Attacker stats
            AttackerPrimaryStat = attacker.Stats.GetTotal(primaryStat),
            AttackerWeaponSkill = attacker.Stats.GetTotal(StatId.WeaponSkill),

            // Target stats
            TargetToughness = target.Stats.GetTotal(StatId.Toughness),
            TargetArmor = target.Stats.GetTotal(StatId.Armor),
            TargetInitiative = target.Stats.GetTotal(StatId.Initiative),

            // Target facing / shield for defense rolls
            TargetIsFacing = target.IsInFrontArc(attacker),
            TargetHasShield = HasShield(target),

            // Random rolls
            DefenseRoll = _random(0, 100),
            CritRoll = _random(0, 100),
            CritVarianceRoll = _random(0, 21) / 100f,  // [0.0, 0.2]
            DamageVariance = _config.DamageVariance,
            DamageVarianceRoll = _random(-100, 101) / 100f, // [-1.0, 1.0]
        };

        return ctx;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  UTILITY
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Computes attack interval in ms from weapon speed and speed modifiers.
    /// Formula: <c>weaponSpeed × 10 / (1 + bonus − reduction)</c>.
    /// </summary>
    internal static long ComputeAttackInterval(UnitEntity entity, ushort weaponSpeed)
    {
        float bonus = entity.Stats.GetTotal(StatId.AutoAttackSpeed) / 100f;
        // Reduced stat handling — in V1, speed reduction comes from a separate "reduced" stat.
        // For V2, we model it as a negative bonus. Total bonus already includes reductions
        // if the stat container is wired correctly.
        float factor = 1f + bonus;
        if (factor < 0.1f)
            factor = 0.1f; // floor to prevent zero/negative intervals

        return (long)(weaponSpeed * 10 / factor);
    }

    private static void ApplyDamage(UnitEntity target, DamageContext ctx)
    {
        if (!ctx.WasDefended && ctx.FinalDamage > 0)
            target.Health.TakeDamage(ctx.FinalDamage);
    }

    private static bool HasShield(UnitEntity entity)
    {
        var offhand = entity.GetWeaponInfo(WeaponSlot.OffHand);
        return offhand is { IsShield: true };
    }

    /// <summary>Starts auto-attacking the given target.</summary>
    public void StartAttack(UnitEntity target)
    {
        Target = target;
        IsAttacking = true;
    }

    /// <summary>Stops auto-attacking.</summary>
    public void StopAttack()
    {
        IsAttacking = false;
        Target = null;
    }
}
