using Core.GameWorld.Components;
using Core.GameWorld.Entities;
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
public sealed record AutoAttackConfig(
    uint MeleeRange = 5,
    uint BaseRangedRange = 90,
    ushort DefaultWeaponSpeed = 200,
    int OffhandBaseChance = 45,
    float OffhandDamagePenalty = 0.9f,
    float AutoAttackStatCoefficient = 0.1f,
    float OffhandStatCoefficient = 0.05f,
    long RetryIntervalMs = 100,
    long RangedLosRetryMs = 1000);

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
/// Delegate for querying equipped weapon information.
/// Returns <c>null</c> if the slot is empty.
/// </summary>
public delegate WeaponInfo? WeaponQuery(UnitEntity entity, WeaponSlot slot);

/// <summary>
/// Delegate for checking distance between two entities.
/// Returns squared distance or actual distance — the component uses ≤ comparison.
/// </summary>
public delegate float DistanceFunc(UnitEntity a, UnitEntity b);

/// <summary>
/// Delegate for line-of-sight check. Returns <c>true</c> if clear LOS.
/// </summary>
public delegate bool LosFunc(UnitEntity attacker, UnitEntity target);

/// <summary>
/// Delegate for facing check. Returns <c>true</c> if <paramref name="target"/>
/// is within the attacker's front arc.
/// </summary>
public delegate bool FacingFunc(UnitEntity attacker, UnitEntity target);

/// <summary>
/// Callback invoked when auto-attack deals damage (main-hand, offhand, ranged).
/// Useful for career resource generation, threat management, etc.
/// </summary>
public delegate void OnAutoAttackHit(UnitEntity attacker, UnitEntity target, DamageContext ctx);

/// <summary>
/// Controls auto-attack timing, range checks, and offhand procs for a <see cref="UnitEntity"/>.
/// Ticked automatically via <see cref="ITickable"/> when attached to an entity.
/// <para>
/// Each tick checks: alive → CC → active → timing → range (melee or ranged)
/// → swing → offhand proc → schedule next attack.
/// </para>
/// </summary>
public sealed class AutoAttackComponent : ComponentBase, ITickable
{
    private readonly AutoAttackConfig _config;
    private readonly WeaponQuery _weaponQuery;
    private readonly DistanceFunc _distanceFunc;
    private readonly LosFunc _losFunc;
    private readonly FacingFunc _facingFunc;
    private readonly Func<int, int, int> _random;

    private long _nextAttackTime;

    /// <summary>Whether auto-attack is currently enabled.</summary>
    public bool IsAttacking { get; set; }

    /// <summary>Current auto-attack target.</summary>
    public UnitEntity? Target { get; set; }

    /// <summary>Optional callback when a swing lands.</summary>
    public OnAutoAttackHit? OnHit { get; set; }

    /// <summary>True if the unit is currently moving (blocks ranged unless overridden).</summary>
    public bool IsMoving { get; set; }

    /// <summary>Whether this unit can shoot on the move (SW scout stance, etc.).</summary>
    public bool MoveAndShoot { get; set; }

    /// <summary>
    /// Creates a new auto-attack component.
    /// </summary>
    /// <param name="config">Tuning parameters.</param>
    /// <param name="weaponQuery">Equipment lookup delegate.</param>
    /// <param name="distanceFunc">Distance check delegate.</param>
    /// <param name="losFunc">Line-of-sight delegate.</param>
    /// <param name="facingFunc">Facing-arc delegate.</param>
    /// <param name="random">RNG returning int in [min, max) — for offhand proc and rolls.</param>
    public AutoAttackComponent(
        AutoAttackConfig config,
        WeaponQuery weaponQuery,
        DistanceFunc distanceFunc,
        LosFunc losFunc,
        FacingFunc facingFunc,
        Func<int, int, int> random)
    {
        _config = config;
        _weaponQuery = weaponQuery;
        _distanceFunc = distanceFunc;
        _losFunc = losFunc;
        _facingFunc = facingFunc;
        _random = random;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  TICK
    // ═══════════════════════════════════════════════════════════════════

    public void Update(long tick)
    {
        var caster = Owner as UnitEntity;
        if (caster is null) return;

        // 1. Dead → stop
        if (caster.Health.IsDead)
        {
            IsAttacking = false;
            return;
        }

        // 2. CC check — disarmed/knocked/staggered
        if ((caster.Buffs.GetActiveCrowdControl() & CrowdControlFlags.NoAutoAttack) != 0)
            return;

        // 3. Must be actively attacking with a target
        if (!IsAttacking || Target is null)
            return;

        // 4. Timing — not yet
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

        // 6. Check melee range first
        float distance = _distanceFunc(caster, target);
        if (distance <= _config.MeleeRange)
        {
            // 6a. Facing check — skip swing if target not in front arc
            if (!_facingFunc(caster, target))
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
        var weapon = _weaponQuery(caster, WeaponSlot.MainHand);
        var weaponDps = weapon?.Dps ?? 0f;
        var weaponSpeed = weapon?.Speed ?? _config.DefaultWeaponSpeed;

        // Build + resolve damage
        var ctx = BuildAutoAttackContext(caster, target, weaponDps, weaponSpeed,
            StatId.Strength, DamageType.Physical, _config.AutoAttackStatCoefficient);

        DamagePipeline.Resolve(ctx);
        ApplyDamage(target, ctx);
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
        var weapon = _weaponQuery(caster, WeaponSlot.Ranged);
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

        // LOS check
        if (!_losFunc(caster, target))
        {
            _nextAttackTime = tick + _config.RangedLosRetryMs;
            return;
        }

        // Build + resolve
        var ctx = BuildAutoAttackContext(caster, target, weapon.Dps, weapon.Speed,
            StatId.BallisticSkill, DamageType.Physical, _config.AutoAttackStatCoefficient);

        DamagePipeline.Resolve(ctx);
        ApplyDamage(target, ctx);
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
        var offhand = _weaponQuery(caster, WeaponSlot.OffHand);
        if (offhand is null || offhand.IsShield || offhand.IsCharm)
            return;

        int bonusChance = caster.Stats.GetTotal(StatId.OffhandProcChance);
        int roll = _random(1, 101); // [1..100]
        if (roll > _config.OffhandBaseChance + bonusChance)
            return;

        // Offhand swing uses main-hand speed for scaling (matches V1)
        var mainWeapon = _weaponQuery(caster, WeaponSlot.MainHand);
        var mhSpeed = mainWeapon?.Speed ?? _config.DefaultWeaponSpeed;

        var ctx = BuildAutoAttackContext(caster, target, offhand.Dps, mhSpeed,
            StatId.Strength, DamageType.Physical, _config.OffhandStatCoefficient);
        ctx.IsOffhand = true;

        DamagePipeline.Resolve(ctx);
        ApplyDamage(target, ctx);
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
            TargetIsFacing = _facingFunc(target, attacker),
            TargetHasShield = HasShield(target),

            // Random rolls
            DefenseRoll = _random(0, 100),
            CritRoll = _random(0, 100),
            CritVarianceRoll = _random(0, 21) / 100f, // [0.0, 0.2]
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
    internal long ComputeAttackInterval(UnitEntity entity, ushort weaponSpeed)
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

    private bool HasShield(UnitEntity entity)
    {
        var offhand = _weaponQuery(entity, WeaponSlot.OffHand);
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
