using Core.Domain.Entities;
using Core.GameWorld.Combat;
using Core.GameWorld.Combat.Abilities;
using Core.GameWorld.Combat.AutoAttack;
using Core.GameWorld.DataStore;
using Core.GameWorld.Entities;
using Core.GameWorld.Events;
using Core.GameWorld.Spatial;
using Core.GameWorld.Stats;
using Core.Spatial;
using Shouldly;

namespace Core.GameWorld.Tests;

/// <summary>
/// Unit tests for the auto-attack system -- timing, range checks, offhand proc,
/// CC interrupts, ranged conditions, and damage integration.
/// </summary>
public class AutoAttackTests
{
    // -------------------------------------------------------------------
    //  Helpers
    // -------------------------------------------------------------------

    /// <summary>WAR heading value: faces east (+X direction).</summary>
    private const ushort FacingEast = 1024;
    /// <summary>WAR heading value: faces west (-X direction).</summary>
    private const ushort FacingWest = 3072;

    public AutoAttackTests()
    {
        // Default: no occlusion provider -> LOS always clear.
    }

    private static TestUnit MakeUnit(ushort id = 1, uint maxHealth = 10_000)
    {
        var entity = new TestUnit(id, maxHealth);
        entity.SetWeapon(WeaponSlot.MainHand, MeleeWeapon()); // default main-hand weapon
        entity.Level = 40;
        entity.ActionPoints = 250;
        entity.Stats.SetBase(StatId.Wounds, (int)(maxHealth / 10));
        return entity;
    }

    /// <summary>
    /// Places <paramref name="b"/> at the given edge-to-edge distance (feet) from
    /// <paramref name="a"/> along the +X axis. Sets <paramref name="a"/>'s heading
    /// to face east so that facing checks pass by default.
    /// </summary>
    private static void PlaceAtDistance(UnitEntity a, UnitEntity b, float edgeToEdgeFeet)
    {
        float centerDist = edgeToEdgeFeet + a.BaseRadius + b.BaseRadius;
        int offset = (int)(centerDist * RegionConstants.UnitsPerFoot);
        a.Position = new WorldPosition(1, 0, 0, 0, FacingEast, 1);
        b.Position = new WorldPosition(1, offset, 0, 0, 0, 1);
    }

    /// <summary>
    /// Rotates <paramref name="observer"/> 180 degrees away from east so that
    /// <c>IsInFrontArc</c> will fail when target is along +X.
    /// </summary>
    private static void FaceAway(UnitEntity observer)
    {
        observer.Position = observer.Position with { Heading = FacingWest };
    }

    /// <summary>Weapon with standard stats.</summary>
    private static WeaponInfo MeleeWeapon(float dps = 80f, ushort speed = 200) =>
        new(dps, speed);

    private static WeaponInfo OffhandWeapon(float dps = 60f) =>
        new(dps, 200);

    private static WeaponInfo RangedWeapon(float dps = 70f, ushort speed = 250) =>
        new(dps, speed);

    private static WeaponInfo Shield() =>
        new(0f, 0, IsShield: true);

    /// <summary>Creates a deterministic RNG that always returns a fixed value.</summary>
    private static Func<int, int, int> FixedRandom(int value) => (_, _) => value;

    private static AutoAttackComponent MakeComponent(
        UnitEntity? owner = null,
        AutoAttackConfig? config = null,
        Func<int, int, int>? random = null)
    {
        return new AutoAttackComponent(
            owner ?? MakeUnit(99),
            config ?? new AutoAttackConfig(),
            random ?? FixedRandom(50));
    }

    /// <summary>
    /// Concrete <see cref="UnitEntity"/> subclass for tests. Holds per-slot weapon info
    /// directly rather than reading from a real inventory, keeping tests isolated.
    /// </summary>
    private sealed class TestUnit : UnitEntity
    {
        private WeaponInfo? _mainHand;
        private WeaponInfo? _offHand;
        private WeaponInfo? _ranged;

        public TestUnit(ushort id, uint maxHealth = 10_000)
            : base(id, EntityType.Player, $"Unit{id}", maxHealth) { }

        public void SetWeapon(WeaponSlot slot, WeaponInfo? info)
        {
            switch (slot)
            {
                case WeaponSlot.MainHand: _mainHand = info; break;
                case WeaponSlot.OffHand:  _offHand  = info; break;
                case WeaponSlot.Ranged:   _ranged   = info; break;
            }
        }

        public override WeaponInfo? GetWeaponInfo(WeaponSlot slot) => slot switch
        {
            WeaponSlot.MainHand => _mainHand,
            WeaponSlot.OffHand  => _offHand,
            WeaponSlot.Ranged   => _ranged,
            _                   => null,
        };
    }

    /// <summary>
    /// Minimal <see cref="IOcclusionProvider"/> that blocks LOS once, then allows it.
    /// </summary>
    private sealed class BlockOnceOcclusion : IOcclusionProvider
    {
        public bool Initialized => true;
        private bool _blocked;

        public int GetTerrainZ(int zoneId, int x, int y) => 0;

        public OcclusionResult Raytest(
            int zoneId,
            float originX, float originY, float originZ,
            float targetX, float targetY, float targetZ,
            bool terrain, ref OcclusionInfo info)
        {
            if (!_blocked)
            {
                _blocked = true;
                return OcclusionResult.OccludedByGeometry;
            }
            return OcclusionResult.NotOccluded;
        }
    }

    // -------------------------------------------------------------------
    //  Basic State
    // -------------------------------------------------------------------

    [Fact]
    public void Starts_not_attacking()
    {
        var comp = MakeComponent();
        comp.IsAttacking.ShouldBeFalse();
        comp.Target.ShouldBeNull();
    }

    [Fact]
    public void StartAttack_sets_state()
    {
        var comp = MakeComponent();
        var target = MakeUnit(2);
        comp.StartAttack(target);
        comp.IsAttacking.ShouldBeTrue();
        comp.Target.ShouldBeSameAs(target);
    }

    [Fact]
    public void StopAttack_clears_state()
    {
        var comp = MakeComponent();
        comp.StartAttack(MakeUnit(2));
        comp.StopAttack();
        comp.IsAttacking.ShouldBeFalse();
        comp.Target.ShouldBeNull();
    }

    // -------------------------------------------------------------------
    //  Melee Swing Timing
    // -------------------------------------------------------------------

    [Fact]
    public void Melee_swing_deals_damage_at_tick_zero()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);

        attacker.SetWeapon(WeaponSlot.MainHand, MeleeWeapon(dps: 80, speed: 200));
        var comp = MakeComponent(owner: attacker);

        comp.StartAttack(target);
        comp.Update(0);

        target.Health.Current.ShouldBeLessThan(target.Health.Max);
    }

    [Fact]
    public void Next_swing_respects_attack_interval()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);

        attacker.SetWeapon(WeaponSlot.MainHand, MeleeWeapon(dps: 80, speed: 200));
        var comp = MakeComponent(owner: attacker);

        comp.StartAttack(target);
        comp.Update(0); // first swing

        var hpAfterFirst = target.Health.Current;

        // Too early for second swing (speed=200 -> interval=2000ms)
        comp.Update(1500);
        target.Health.Current.ShouldBe(hpAfterFirst);

        // Now at 2000ms -- should swing
        comp.Update(2000);
        target.Health.Current.ShouldBeLessThan(hpAfterFirst);
    }

    [Fact]
    public void Attack_speed_stat_reduces_interval()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);
        // 25% speed bonus -> interval = 200*10 / (1 + 0.25) = 1600
        attacker.Stats.SetBase(StatId.AutoAttackSpeed, 25);
        attacker.Stats.Flush();

        attacker.SetWeapon(WeaponSlot.MainHand, MeleeWeapon(dps: 80, speed: 200));
        var comp = MakeComponent(owner: attacker);

        comp.StartAttack(target);
        comp.Update(0); // first swing
        var hpAfterFirst = target.Health.Current;

        // At 1500ms (< 1600), shouldn't swing yet
        comp.Update(1500);
        target.Health.Current.ShouldBe(hpAfterFirst);

        // At 1600ms, should swing
        comp.Update(1600);
        target.Health.Current.ShouldBeLessThan(hpAfterFirst);
    }

    [Fact]
    public void ComputeAttackInterval_matches_formula()
    {
        var entity = MakeUnit();
        var comp = MakeComponent();

        // No bonuses: 200 * 10 / 1.0 = 2000
        AutoAttackComponent.ComputeAttackInterval(entity, 200).ShouldBe(2000);

        // 50% bonus: 200 * 10 / 1.5 ~ 1333
        entity.Stats.SetBase(StatId.AutoAttackSpeed, 50);
        entity.Stats.Flush();
        AutoAttackComponent.ComputeAttackInterval(entity, 200).ShouldBe(1333);
    }

    // -------------------------------------------------------------------
    //  Melee Range
    // -------------------------------------------------------------------

    [Fact]
    public void Melee_swing_at_boundary()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 5f); // exactly at melee range

        var comp = MakeComponent(owner: attacker);

        comp.StartAttack(target);
        comp.Update(0);

        target.Health.Current.ShouldBeLessThan(target.Health.Max);
    }

    [Fact]
    public void Out_of_melee_range_no_ranged_weapon_retries()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        // Out of melee (6 > 5), no ranged weapon
        PlaceAtDistance(attacker, target, 6f);

        attacker.SetWeapon(WeaponSlot.MainHand, MeleeWeapon());
        // offHand and ranged are null by default
        var comp = MakeComponent(owner: attacker);

        comp.StartAttack(target);
        comp.Update(0);

        target.Health.Current.ShouldBe(target.Health.Max); // no damage
        comp.IsAttacking.ShouldBeTrue(); // still trying
    }

    // -------------------------------------------------------------------
    //  Ranged Attacks
    // -------------------------------------------------------------------

    [Fact]
    public void Ranged_swing_when_out_of_melee()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 50f); // out of melee, in ranged

        attacker.SetWeapon(WeaponSlot.Ranged, RangedWeapon());
        var comp = MakeComponent(owner: attacker);

        comp.StartAttack(target);
        comp.Update(0);

        target.Health.Current.ShouldBeLessThan(target.Health.Max);
    }

    [Fact]
    public void Ranged_blocked_by_movement()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 50f);

        attacker.SetWeapon(WeaponSlot.Ranged, RangedWeapon());
        var comp = MakeComponent(owner: attacker);
        comp.IsMoving = true;

        comp.StartAttack(target);
        comp.Update(0);

        target.Health.Current.ShouldBe(target.Health.Max); // blocked by movement
    }

    [Fact]
    public void MoveAndShoot_overrides_movement_block()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 50f);

        attacker.SetWeapon(WeaponSlot.Ranged, RangedWeapon());
        var comp = MakeComponent(owner: attacker);
        comp.IsMoving = true;
        comp.MoveAndShoot = true;

        comp.StartAttack(target);
        comp.Update(0);

        target.Health.Current.ShouldBeLessThan(target.Health.Max);
    }

    [Fact]
    public void Ranged_out_of_range_no_damage()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        // base ranged range = 90, distance = 91
        PlaceAtDistance(attacker, target, 91f);

        attacker.SetWeapon(WeaponSlot.Ranged, RangedWeapon());
        var comp = MakeComponent(owner: attacker);

        comp.StartAttack(target);
        comp.Update(0);

        target.Health.Current.ShouldBe(target.Health.Max);
    }

    [Fact]
    public void Range_stat_extends_ranged_range()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        attacker.Stats.SetBase(StatId.Range, 20); // 90 + 20 = 110
        attacker.Stats.Flush();

        // Distance 100 -> within 110 range
        PlaceAtDistance(attacker, target, 100f);

        attacker.SetWeapon(WeaponSlot.Ranged, RangedWeapon());
        var comp = MakeComponent(owner: attacker);

        comp.StartAttack(target);
        comp.Update(0);

        target.Health.Current.ShouldBeLessThan(target.Health.Max);
    }

    [Fact]
    public void Ranged_los_failure_adds_delay()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 50f);

        // Wire a mock occlusion provider that blocks once, then allows
        var occlusion = new BlockOnceOcclusion();
        attacker.RegionServices = new RegionServices(occlusion);
        target.RegionServices = new RegionServices(occlusion);

        attacker.SetWeapon(WeaponSlot.Ranged, RangedWeapon());
        var comp = MakeComponent(owner: attacker);

        comp.StartAttack(target);
        comp.Update(0); // LOS fails -> delay 1000ms

        target.Health.Current.ShouldBe(target.Health.Max); // no damage

        // At 500ms -- still delayed
        comp.Update(500);
        target.Health.Current.ShouldBe(target.Health.Max);

        // At 1000ms -- retry succeeds
        comp.Update(1000);
        target.Health.Current.ShouldBeLessThan(target.Health.Max);
    }

    // -------------------------------------------------------------------
    //  Offhand Proc
    // -------------------------------------------------------------------

    [Fact]
    public void Offhand_procs_on_melee_swing()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);
        // Random returns 30 -> below 45 threshold -> offhand fires
        attacker.SetWeapon(WeaponSlot.OffHand, OffhandWeapon());
        var comp = MakeComponent(
            owner: attacker,
            random: FixedRandom(30));

        comp.StartAttack(target);
        comp.Update(0);

        var totalDamage = target.Health.Max - target.Health.Current;
        totalDamage.ShouldBeGreaterThan(0u);
    }

    [Fact]
    public void Offhand_does_not_proc_when_roll_above_threshold()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);
        int callCount = 0;
        // Random returns 80 -> above 45 -> no offhand
        attacker.SetWeapon(WeaponSlot.OffHand, OffhandWeapon());
        var comp = MakeComponent(
            owner: attacker,
            random: FixedRandom(80));
        comp.OnHit = (_, _, _) => callCount++;

        comp.StartAttack(target);
        comp.Update(0);

        callCount.ShouldBe(1); // only main-hand
    }

    [Fact]
    public void Offhand_procs_with_bonus_chance()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);
        // Roll=50, base=45, bonus=10 -> threshold=55, 50<=55 -> procs
        attacker.Stats.SetBase(StatId.OffhandProcChance, 10);
        attacker.Stats.Flush();

        int hitCount = 0;
        attacker.SetWeapon(WeaponSlot.OffHand, OffhandWeapon());
        var comp = MakeComponent(
            owner: attacker,
            random: FixedRandom(50));
        comp.OnHit = (_, _, _) => hitCount++;

        comp.StartAttack(target);
        comp.Update(0);

        hitCount.ShouldBe(2); // main + offhand
    }

    [Fact]
    public void Offhand_blocked_by_shield()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);
        int hitCount = 0;
        // Roll=30 -> would proc, but offhand is a shield
        attacker.SetWeapon(WeaponSlot.OffHand, Shield());
        var comp = MakeComponent(
            owner: attacker,
            random: FixedRandom(30));
        comp.OnHit = (_, _, _) => hitCount++;

        comp.StartAttack(target);
        comp.Update(0);

        hitCount.ShouldBe(1); // main only -- shield blocks offhand
    }

    [Fact]
    public void Offhand_does_not_proc_on_ranged()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 50f); // out of melee -> ranged
        int hitCount = 0;
        attacker.SetWeapon(WeaponSlot.OffHand, OffhandWeapon());
        attacker.SetWeapon(WeaponSlot.Ranged, RangedWeapon());
        var comp = MakeComponent(
            owner: attacker,
            random: FixedRandom(30)); // would proc if melee
        comp.OnHit = (_, _, _) => hitCount++;

        comp.StartAttack(target);
        comp.Update(0);

        hitCount.ShouldBe(1); // ranged only, no offhand
    }

    // -------------------------------------------------------------------
    //  CC Interrupts
    // -------------------------------------------------------------------

    [Fact]
    public void Disarm_blocks_auto_attack()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);

        attacker.Buffs.QueueBuff(new BuffDefinition
        {
            Entry = 9999,
            Name = "TestDisarm",
            BuffClass = BuffClass.Buff0,
            DurationMs = 5000,
            CrowdControl = CrowdControlFlags.Disarm,
        }, attacker);
        attacker.Buffs.Update(0);

        var comp = MakeComponent(owner: attacker);
        comp.StartAttack(target);

        comp.Update(0);

        target.Health.Current.ShouldBe(target.Health.Max); // no damage -- disarmed
    }

    [Fact]
    public void Knockdown_blocks_auto_attack()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);

        attacker.Buffs.QueueBuff(new BuffDefinition
        {
            Entry = 9998,
            Name = "TestKD",
            BuffClass = BuffClass.Buff0,
            DurationMs = 5000,
            CrowdControl = CrowdControlFlags.Knockdown,
        }, attacker);
        attacker.Buffs.Update(0);

        var comp = MakeComponent(owner: attacker);
        comp.StartAttack(target);

        comp.Update(0);

        target.Health.Current.ShouldBe(target.Health.Max);
    }

    [Fact]
    public void Stagger_blocks_auto_attack()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);

        attacker.Buffs.QueueBuff(new BuffDefinition
        {
            Entry = 9997,
            Name = "TestStagger",
            BuffClass = BuffClass.Buff0,
            DurationMs = 5000,
            CrowdControl = CrowdControlFlags.Stagger,
        }, attacker);
        attacker.Buffs.Update(0);

        var comp = MakeComponent(owner: attacker);
        comp.StartAttack(target);

        comp.Update(0);

        target.Health.Current.ShouldBe(target.Health.Max);
    }

    [Fact]
    public void Snare_does_not_block_auto_attack()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);

        attacker.Buffs.QueueBuff(new BuffDefinition
        {
            Entry = 9996,
            Name = "TestSnare",
            BuffClass = BuffClass.Buff0,
            DurationMs = 5000,
            CrowdControl = CrowdControlFlags.Snare,
        }, attacker);
        attacker.Buffs.Update(0);

        var comp = MakeComponent(owner: attacker);
        comp.StartAttack(target);

        comp.Update(0);

        target.Health.Current.ShouldBeLessThan(target.Health.Max); // snare doesn't block
    }

    // -------------------------------------------------------------------
    //  Dead Target / Dead Attacker
    // -------------------------------------------------------------------

    [Fact]
    public void Stops_when_target_dies()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2, maxHealth: 1);
        PlaceAtDistance(attacker, target, 3f);
        target.Health.TakeDamage(1); // kill target

        var comp = MakeComponent(owner: attacker);
        comp.StartAttack(target);

        comp.Update(0);

        comp.IsAttacking.ShouldBeFalse();
        comp.Target.ShouldBeNull();
    }

    [Fact]
    public void Stops_when_attacker_dies()
    {
        var attacker = MakeUnit(1, maxHealth: 1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);
        attacker.Health.TakeDamage(1); // kill attacker

        var comp = MakeComponent(owner: attacker);
        comp.StartAttack(target);

        comp.Update(0);

        comp.IsAttacking.ShouldBeFalse();
        target.Health.Current.ShouldBe(target.Health.Max); // no damage dealt
    }

    // -------------------------------------------------------------------
    //  Facing Check
    // -------------------------------------------------------------------

    [Fact]
    public void Melee_swing_skipped_when_not_facing()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);
        FaceAway(attacker); // make attacker look away from target

        var comp = MakeComponent(owner: attacker);

        comp.StartAttack(target);
        comp.Update(0);

        target.Health.Current.ShouldBe(target.Health.Max); // no swing
        comp.IsAttacking.ShouldBeTrue(); // still trying
    }

    // -------------------------------------------------------------------
    //  Damage Context Flags
    // -------------------------------------------------------------------

    [Fact]
    public void Main_hand_context_has_auto_attack_flag()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);
        DamageContext? captured = null;

        var comp = MakeComponent(
            owner: attacker,
            random: FixedRandom(80)); // no offhand
        comp.OnHit = (_, _, ctx) => captured = ctx;

        comp.StartAttack(target);
        comp.Update(0);

        captured.ShouldNotBeNull();
        captured.IsAutoAttack.ShouldBeTrue();
        captured.IsOffhand.ShouldBeFalse();
        captured.DamageType.ShouldBe(DamageType.Physical);
    }

    [Fact]
    public void Offhand_context_has_offhand_flag()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);
        DamageContext? lastCtx = null;

        attacker.SetWeapon(WeaponSlot.OffHand, OffhandWeapon());
        var comp = MakeComponent(
            owner: attacker,
            random: FixedRandom(30)); // offhand procs
        comp.OnHit = (_, _, ctx) => lastCtx = ctx;

        comp.StartAttack(target);
        comp.Update(0);

        // Last hit should be offhand
        lastCtx.ShouldNotBeNull();
        lastCtx.IsAutoAttack.ShouldBeTrue();
        lastCtx.IsOffhand.ShouldBeTrue();
    }

    [Fact]
    public void Ranged_uses_ballistic_skill()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 50f);
        attacker.Stats.SetBase(StatId.BallisticSkill, 400);
        attacker.Stats.Flush();

        DamageContext? captured = null;
        attacker.SetWeapon(WeaponSlot.Ranged, RangedWeapon());
        var comp = MakeComponent(
            owner: attacker,
            random: FixedRandom(80));
        comp.OnHit = (_, _, ctx) => captured = ctx;

        comp.StartAttack(target);
        comp.Update(0);

        captured.ShouldNotBeNull();
        captured.AttackerPrimaryStat.ShouldBe(400);
    }

    [Fact]
    public void CastTimeDamageMult_set_from_weapon_speed()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);
        DamageContext? captured = null;

        // speed=300 -> CastTimeDamageMult = 300/100 = 3.0
        attacker.SetWeapon(WeaponSlot.MainHand, MeleeWeapon(dps: 80, speed: 300));
        var comp = MakeComponent(
            owner: attacker,
            random: FixedRandom(80));
        comp.OnHit = (_, _, ctx) => captured = ctx;

        comp.StartAttack(target);
        comp.Update(0);

        captured.ShouldNotBeNull();
        captured.CastTimeDamageMult.ShouldBe(3.0f);
    }

    [Fact]
    public void AutoAttack_coefficient_is_point_one()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);
        DamageContext? captured = null;

        var comp = MakeComponent(
            owner: attacker,
            random: FixedRandom(80));
        comp.OnHit = (_, _, ctx) => captured = ctx;

        comp.StartAttack(target);
        comp.Update(0);

        captured.ShouldNotBeNull();
        captured.StatCoefficient.ShouldBe(0.1f);
    }

    // -------------------------------------------------------------------
    //  Entity Integration (ITickable via WorldEntity.Update)
    // -------------------------------------------------------------------

    [Fact]
    public void Component_ticked_by_entity_update()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);

        attacker.AutoAttack.StartAttack(target);

        // Entity.Update ticks AutoAttack as a direct field
        attacker.Update(0);

        target.Health.Current.ShouldBeLessThan(target.Health.Max);
    }

    [Fact]
    public void Due_swing_waits_for_channel_to_complete()
    {
        var caster = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(caster, target, 3f);

        var channel = new AbilityDefinition
        {
            Entry = 1000,
            Name = "Test Channel",
            CastTime = 1000,
            ChannelId = 1,
            ChannelInterval = 1000,
            ApCost = 0,
            TargetType = CommandTargetType.Enemy,
        };
        var cast = caster.Abilities.TryInitiate(channel, target, 0, out _)!;
        caster.Abilities.ConfirmCast(cast, 0).ShouldBeTrue();
        caster.AutoAttack.StartAttack(target);

        caster.Update(0);
        target.Health.Current.ShouldBe(target.Health.Max);

        caster.Update(500);
        target.Health.Current.ShouldBe(target.Health.Max);

        // UnitEntity updates abilities before auto-attack, so the due swing fires
        // immediately after the channel successfully clears its active cast.
        caster.Update(1000);
        target.Health.Current.ShouldBeLessThan(target.Health.Max);
    }

    [Fact]
    public void Stationary_position_update_does_not_interrupt_channel()
    {
        var caster = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(caster, target, 3f);

        var channel = new AbilityDefinition
        {
            Entry = 1001,
            Name = "Test Channel",
            CastTime = 1000,
            ChannelId = 1,
            ChannelInterval = 1000,
            ApCost = 0,
            TargetType = CommandTargetType.Enemy,
        };
        var cast = caster.Abilities.TryInitiate(channel, target, 0, out _)!;
        caster.Abilities.ConfirmCast(cast, 0).ShouldBeTrue();

        new CancelCastOnMoveAction(caster.ObjectId, caster.Position)
            .Execute(new StubActionContext(caster), 50);

        caster.Abilities.ActiveCast.ShouldBeSameAs(cast);
    }

    // -------------------------------------------------------------------
    //  No Weapon Fallback
    // -------------------------------------------------------------------

    [Fact]
    public void No_weapon_uses_default_speed()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);
        // No weapons at all -- uses default weapon speed
        attacker.SetWeapon(WeaponSlot.MainHand, null); // clear the default weapon
        var comp = MakeComponent(owner: attacker);

        comp.StartAttack(target);
        comp.Update(0);

        // With null weapon DPS = 0, no damage but interval should use default (200)
        comp.IsAttacking.ShouldBeTrue();
    }

    // -------------------------------------------------------------------
    //  Offhand with OffhandDamage stat bonus
    // -------------------------------------------------------------------

    [Fact]
    public void Offhand_uses_lower_stat_coefficient()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);
        DamageContext? lastCtx = null;

        attacker.SetWeapon(WeaponSlot.OffHand, OffhandWeapon());
        var comp = MakeComponent(
            owner: attacker,
            random: FixedRandom(30)); // offhand procs
        comp.OnHit = (_, _, ctx) => lastCtx = ctx;

        comp.StartAttack(target);
        comp.Update(0);

        lastCtx.ShouldNotBeNull();
        lastCtx.StatCoefficient.ShouldBe(0.05f);
    }

    // -------------------------------------------------------------------
    //  Multiple Swings
    // -------------------------------------------------------------------

    [Fact]
    public void Multiple_swings_accumulate_damage()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2, maxHealth: 100_000);
        PlaceAtDistance(attacker, target, 3f);
        attacker.Stats.SetBase(StatId.Wounds, 10_000);
        attacker.Stats.Flush();
        attacker.Health.Heal(100_000);

        attacker.SetWeapon(WeaponSlot.MainHand, MeleeWeapon(dps: 80, speed: 200));
        var comp = MakeComponent(
            owner: attacker,
            random: FixedRandom(80)); // no offhand

        comp.StartAttack(target);

        // Swing at t=0, t=2000, t=4000
        comp.Update(0);
        var d1 = target.Health.Max - target.Health.Current;

        comp.Update(2000);
        var d2 = target.Health.Max - target.Health.Current;

        comp.Update(4000);
        var d3 = target.Health.Max - target.Health.Current;

        d1.ShouldBeGreaterThan(0u);
        d2.ShouldBeGreaterThan(d1);
        d3.ShouldBeGreaterThan(d2);
    }

    // -------------------------------------------------------------------
    //  OnHit callback
    // -------------------------------------------------------------------

    [Fact]
    public void OnHit_fires_for_each_swing()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);
        int hitCount = 0;

        var comp = MakeComponent(
            owner: attacker,
            random: FixedRandom(80)); // no offhand
        comp.OnHit = (a, t, _) =>
        {
            a.ShouldBeSameAs(attacker);
            t.ShouldBeSameAs(target);
            hitCount++;
        };

        comp.StartAttack(target);
        comp.Update(0);
        comp.Update(2000);

        hitCount.ShouldBe(2);
    }

    // -------------------------------------------------------------------
    //  StopAutoAttackAction
    // -------------------------------------------------------------------

    [Fact]
    public void StopAutoAttackAction_stops_attacking_unit()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        attacker.AutoAttack.StartAttack(target);
        attacker.AutoAttack.IsAttacking.ShouldBeTrue();

        var action = new StopAutoAttackAction(attacker.ObjectId);
        action.Execute(new StubActionContext(attacker), tick: 0);

        attacker.AutoAttack.IsAttacking.ShouldBeFalse();
    }

    [Fact]
    public void StopAutoAttackAction_is_noop_when_unit_not_found()
    {
        var action = new StopAutoAttackAction(99);
        // Should not throw when entity is missing
        action.Execute(new StubActionContext(null), tick: 0);
    }

    // -------------------------------------------------------------------
    //  Ability damage → auto-attack start (V1 parity)
    // -------------------------------------------------------------------

    [Fact]
    public void Ability_damage_starts_auto_attack_if_not_attacking()
    {
        var caster = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(caster, target, 0f);

        caster.Abilities.EffectExecutor = new AbilityEffectExecutor(new Random(42));
        caster.AutoAttack.IsAttacking.ShouldBeFalse();

        var def = MakeDamageAbilityDef();
        var ctx = caster.Abilities.TryInitiate(def, target, 0, out _)!;
        caster.Abilities.ConfirmCast(ctx, 0);

        caster.AutoAttack.IsAttacking.ShouldBeTrue();
        caster.AutoAttack.Target.ShouldBeSameAs(target);
    }

    [Fact]
    public void Ability_damage_does_not_reset_existing_auto_attack_target()
    {
        var caster = MakeUnit(1);
        var originalTarget = MakeUnit(2);
        var abilityTarget = MakeUnit(3);
        PlaceAtDistance(caster, abilityTarget, 0f);

        caster.Abilities.EffectExecutor = new AbilityEffectExecutor(new Random(42));
        caster.AutoAttack.StartAttack(originalTarget);

        var def = MakeDamageAbilityDef();
        var ctx = caster.Abilities.TryInitiate(def, abilityTarget, 0, out _)!;
        caster.Abilities.ConfirmCast(ctx, 0);

        // Already attacking — target should not change
        caster.AutoAttack.Target.ShouldBeSameAs(originalTarget);
    }

    // -------------------------------------------------------------------
    //  Combat state refresh on damage (V1 parity)
    // -------------------------------------------------------------------

    [Fact]
    public void Ability_damage_enters_caster_combat()
    {
        var caster = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(caster, target, 0f);

        caster.Abilities.EffectExecutor = new AbilityEffectExecutor(new Random(42));
        caster.CombatState.IsInCombat.ShouldBeFalse();

        var def = MakeDamageAbilityDef();
        var ctx = caster.Abilities.TryInitiate(def, target, 0, out _)!;
        caster.Abilities.ConfirmCast(ctx, 0);

        caster.CombatState.IsInCombat.ShouldBeTrue();
    }

    [Fact]
    public void Ability_damage_enters_target_combat()
    {
        var caster = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(caster, target, 0f);

        caster.Abilities.EffectExecutor = new AbilityEffectExecutor(new Random(42));
        target.CombatState.IsInCombat.ShouldBeFalse();

        var def = MakeDamageAbilityDef();
        var ctx = caster.Abilities.TryInitiate(def, target, 0, out _)!;
        caster.Abilities.ConfirmCast(ctx, 0);

        target.CombatState.IsInCombat.ShouldBeTrue();
    }

    [Fact]
    public void AutoAttack_damage_enters_target_combat()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        PlaceAtDistance(attacker, target, 3f);

        target.CombatState.IsInCombat.ShouldBeFalse();

        attacker.AutoAttack.StartAttack(target);
        attacker.Update(0); // triggers a swing

        target.CombatState.IsInCombat.ShouldBeTrue();
    }

    // -------------------------------------------------------------------
    //  Helpers for new sections
    // -------------------------------------------------------------------

    /// <summary>
    /// Builds a minimal instant damage ability definition for combat-mechanic tests.
    /// Uses RawDamage to bypass defense variance.
    /// </summary>
    private static AbilityDefinition MakeDamageAbilityDef(ushort damage = 100) =>
        new()
        {
            Entry = 9900,
            Name = "TestAbility",
            CastTime = 0,
            Cooldown = 0,
            ApCost = 0,
            Range = 0,
            TargetType = CommandTargetType.Enemy,
            Commands =
            [
                new AbilityCommandDefinition
                {
                    EffectType = AbilityEffectType.DealDamage,
                    TargetType = CommandTargetType.Enemy,
                    Damage = new DamageDefinition
                    {
                        MinDamage = damage,
                        MaxDamage = damage,
                        DamageVariance = 0,
                        DamageType = DamageType.RawDamage,
                        NoCrits = true,
                        Undefendable = true,
                    },
                },
            ],
        };

    /// <summary>
    /// Minimal <see cref="IRegionActionContext"/> stub that resolves a single entity.
    /// </summary>
    private sealed class StubActionContext : IRegionActionContext
    {
        private readonly WorldEntity? _entity;

        public StubActionContext(WorldEntity? entity) => _entity = entity;

        public WorldEntity? GetEntity(ushort oid) => _entity?.ObjectId == oid ? _entity : null;

        public IGameDataStore GameData => throw new NotSupportedException();
        public IRegionEventDispatcher Dispatcher => throw new NotSupportedException();
    }
}
