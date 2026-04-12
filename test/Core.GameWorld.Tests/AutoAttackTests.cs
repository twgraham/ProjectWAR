using Core.Domain.Entities;
using Core.GameWorld.Combat;
using Core.GameWorld.Combat.AutoAttack;
using Core.GameWorld.Entities;
using Core.GameWorld.Stats;
using Shouldly;

namespace Core.GameWorld.Tests;

/// <summary>
/// Unit tests for Step 8: Auto-attack system â€” timing, range checks, offhand proc,
/// CC interrupts, ranged conditions, and damage integration.
/// </summary>
public class AutoAttackTests
{
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Helpers
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    private static PlayerEntity MakeUnit(ushort id = 1, uint maxHealth = 10_000)
    {
        var entity = new PlayerEntity(id,
            new Character { CharacterId = id, Name = $"Unit{id}" }, maxHealth);
        entity.Level = 40;
        entity.ActionPoints = 250;
        entity.Stats.SetBase(StatId.Wounds, (int)(maxHealth / 10));
        return entity;
    }

    /// <summary>Always-true LOS check.</summary>
    private static bool AlwaysLos(UnitEntity a, UnitEntity b) => true;

    /// <summary>Always-true facing check.</summary>
    private static bool AlwaysFacing(UnitEntity a, UnitEntity b) => true;

    /// <summary>Never-true LOS check.</summary>
    private static bool NeverLos(UnitEntity a, UnitEntity b) => false;

    /// <summary>Never-true facing check.</summary>
    private static bool NeverFacing(UnitEntity a, UnitEntity b) => false;

    /// <summary>Returns a fixed distance between any two entities.</summary>
    private static DistanceFunc FixedDistance(float d) => (_, _) => d;

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

    /// <summary>Creates a weapon query from slot lambda.</summary>
    private static WeaponQuery SimpleWeapons(
        WeaponInfo? mainHand = null,
        WeaponInfo? offHand = null,
        WeaponInfo? ranged = null)
    {
        return (_, slot) => slot switch
        {
            WeaponSlot.MainHand => mainHand,
            WeaponSlot.OffHand => offHand,
            WeaponSlot.Ranged => ranged,
            _ => null,
        };
    }

    private static AutoAttackComponent MakeComponent(
        AutoAttackConfig? config = null,
        WeaponQuery? weapons = null,
        DistanceFunc? distance = null,
        LosFunc? los = null,
        FacingFunc? facing = null,
        Func<int, int, int>? random = null)
    {
        return new AutoAttackComponent(
            config ?? new AutoAttackConfig(),
            weapons ?? SimpleWeapons(MeleeWeapon()),
            distance ?? FixedDistance(3f), // defaults to melee range
            los ?? AlwaysLos,
            facing ?? AlwaysFacing,
            random ?? FixedRandom(50)); // default: below 45 offhand threshold
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Basic State
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

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

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Melee Swing Timing
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Melee_swing_deals_damage_at_tick_zero()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(dps: 80, speed: 200)),
            distance: FixedDistance(3f));
        attacker.Attach(comp);

        comp.StartAttack(target);
        comp.Update(0);

        // Should have dealt some damage
        target.Health.Current.ShouldBeLessThan(target.Health.Max);
    }

    [Fact]
    public void Next_swing_respects_attack_interval()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(dps: 80, speed: 200)),
            distance: FixedDistance(3f));
        attacker.Attach(comp);

        comp.StartAttack(target);
        comp.Update(0); // first swing

        var hpAfterFirst = target.Health.Current;

        // Too early for second swing (speed=200 â†’ interval=2000ms)
        comp.Update(1500);
        target.Health.Current.ShouldBe(hpAfterFirst); // no new swing

        // Now at 2000ms â€” should swing
        comp.Update(2000);
        target.Health.Current.ShouldBeLessThan(hpAfterFirst);
    }

    [Fact]
    public void Attack_speed_stat_reduces_interval()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        // 25% speed bonus â†’ interval = 200*10 / (1 + 0.25) = 1600
        attacker.Stats.SetBase(StatId.AutoAttackSpeed, 25);
        attacker.Stats.Flush();

        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(dps: 80, speed: 200)),
            distance: FixedDistance(3f));
        attacker.Attach(comp);

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
        entity.Attach(comp);

        // No bonuses: 200 * 10 / 1.0 = 2000
        comp.ComputeAttackInterval(entity, 200).ShouldBe(2000);

        // 50% bonus: 200 * 10 / 1.5 â‰ˆ 1333
        entity.Stats.SetBase(StatId.AutoAttackSpeed, 50);
        entity.Stats.Flush();
        comp.ComputeAttackInterval(entity, 200).ShouldBe(1333);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Melee Range
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Melee_swing_at_boundary()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon()),
            distance: FixedDistance(5f)); // exactly at melee range
        attacker.Attach(comp);

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
        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(), null, null),
            distance: FixedDistance(6f));
        attacker.Attach(comp);

        comp.StartAttack(target);
        comp.Update(0);

        target.Health.Current.ShouldBe(target.Health.Max); // no damage
        comp.IsAttacking.ShouldBeTrue(); // still trying
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Ranged Attacks
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Ranged_swing_when_out_of_melee()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(), null, RangedWeapon()),
            distance: FixedDistance(50f)); // out of melee, in ranged
        attacker.Attach(comp);

        comp.StartAttack(target);
        comp.Update(0);

        target.Health.Current.ShouldBeLessThan(target.Health.Max);
    }

    [Fact]
    public void Ranged_blocked_by_movement()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(), null, RangedWeapon()),
            distance: FixedDistance(50f));
        attacker.Attach(comp);
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
        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(), null, RangedWeapon()),
            distance: FixedDistance(50f));
        attacker.Attach(comp);
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
        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(), null, RangedWeapon()),
            distance: FixedDistance(91f));
        attacker.Attach(comp);

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

        // Distance 100 â†’ within 110 range
        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(), null, RangedWeapon()),
            distance: FixedDistance(100f));
        attacker.Attach(comp);

        comp.StartAttack(target);
        comp.Update(0);

        target.Health.Current.ShouldBeLessThan(target.Health.Max);
    }

    [Fact]
    public void Ranged_los_failure_adds_delay()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        bool losBlockedOnce = true;
        bool LosCheck(UnitEntity a, UnitEntity b)
        {
            if (losBlockedOnce)
            {
                losBlockedOnce = false;
                return false;
            }
            return true;
        }

        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(), null, RangedWeapon()),
            distance: FixedDistance(50f),
            los: LosCheck);
        attacker.Attach(comp);

        comp.StartAttack(target);
        comp.Update(0); // LOS fails â†’ delay 1000ms

        target.Health.Current.ShouldBe(target.Health.Max); // no damage

        // At 500ms â€” still delayed
        comp.Update(500);
        target.Health.Current.ShouldBe(target.Health.Max);

        // At 1000ms â€” retry succeeds
        comp.Update(1000);
        target.Health.Current.ShouldBeLessThan(target.Health.Max);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Offhand Proc
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Offhand_procs_on_melee_swing()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        // Random returns 30 â†’ below 45 threshold â†’ offhand fires
        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(), OffhandWeapon()),
            distance: FixedDistance(3f),
            random: FixedRandom(30));
        attacker.Attach(comp);

        comp.StartAttack(target);
        comp.Update(0);

        // Both main-hand and offhand should have hit
        // With dps=80 + offhand dps=60 at low stats, should deal noticeable damage
        var totalDamage = target.Health.Max - target.Health.Current;
        totalDamage.ShouldBeGreaterThan(0u);
    }

    [Fact]
    public void Offhand_does_not_proc_when_roll_above_threshold()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        int callCount = 0;
        // Random returns 80 â†’ above 45 â†’ no offhand
        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(), OffhandWeapon()),
            distance: FixedDistance(3f),
            random: FixedRandom(80));
        attacker.Attach(comp);
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
        // Roll=50, base=45, bonus=10 â†’ threshold=55, 50â‰¤55 â†’ procs
        attacker.Stats.SetBase(StatId.OffhandProcChance, 10);
        attacker.Stats.Flush();

        int hitCount = 0;
        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(), OffhandWeapon()),
            distance: FixedDistance(3f),
            random: FixedRandom(50));
        attacker.Attach(comp);
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
        int hitCount = 0;
        // Roll=30 â†’ would proc, but offhand is a shield
        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(), Shield()),
            distance: FixedDistance(3f),
            random: FixedRandom(30));
        attacker.Attach(comp);
        comp.OnHit = (_, _, _) => hitCount++;

        comp.StartAttack(target);
        comp.Update(0);

        hitCount.ShouldBe(1); // main only â€” shield blocks offhand
    }

    [Fact]
    public void Offhand_does_not_proc_on_ranged()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        int hitCount = 0;
        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(), OffhandWeapon(), RangedWeapon()),
            distance: FixedDistance(50f), // out of melee â†’ ranged
            random: FixedRandom(30)); // would proc if melee
        attacker.Attach(comp);
        comp.OnHit = (_, _, _) => hitCount++;

        comp.StartAttack(target);
        comp.Update(0);

        hitCount.ShouldBe(1); // ranged only, no offhand
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  CC Interrupts
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Disarm_blocks_auto_attack()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);

        // Give attacker a disarm debuff
        attacker.Buffs.QueueBuff(new BuffDefinition
        {
            Entry = 9999,
            Name = "TestDisarm",
            BuffClass = BuffClass.Buff0,
            DurationMs = 5000,
            CrowdControl = CrowdControlFlags.Disarm,
        }, attacker);
        attacker.Buffs.Update(0);

        var comp = MakeComponent(distance: FixedDistance(3f));
        attacker.Attach(comp);
        comp.StartAttack(target);

        comp.Update(0);

        target.Health.Current.ShouldBe(target.Health.Max); // no damage â€” disarmed
    }

    [Fact]
    public void Knockdown_blocks_auto_attack()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);

        attacker.Buffs.QueueBuff(new BuffDefinition
        {
            Entry = 9998,
            Name = "TestKD",
            BuffClass = BuffClass.Buff0,
            DurationMs = 5000,
            CrowdControl = CrowdControlFlags.Knockdown,
        }, attacker);
        attacker.Buffs.Update(0);

        var comp = MakeComponent(distance: FixedDistance(3f));
        attacker.Attach(comp);
        comp.StartAttack(target);

        comp.Update(0);

        target.Health.Current.ShouldBe(target.Health.Max);
    }

    [Fact]
    public void Stagger_blocks_auto_attack()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);

        attacker.Buffs.QueueBuff(new BuffDefinition
        {
            Entry = 9997,
            Name = "TestStagger",
            BuffClass = BuffClass.Buff0,
            DurationMs = 5000,
            CrowdControl = CrowdControlFlags.Stagger,
        }, attacker);
        attacker.Buffs.Update(0);

        var comp = MakeComponent(distance: FixedDistance(3f));
        attacker.Attach(comp);
        comp.StartAttack(target);

        comp.Update(0);

        target.Health.Current.ShouldBe(target.Health.Max);
    }

    [Fact]
    public void Snare_does_not_block_auto_attack()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);

        attacker.Buffs.QueueBuff(new BuffDefinition
        {
            Entry = 9996,
            Name = "TestSnare",
            BuffClass = BuffClass.Buff0,
            DurationMs = 5000,
            CrowdControl = CrowdControlFlags.Snare,
        }, attacker);
        attacker.Buffs.Update(0);

        var comp = MakeComponent(distance: FixedDistance(3f));
        attacker.Attach(comp);
        comp.StartAttack(target);

        comp.Update(0);

        target.Health.Current.ShouldBeLessThan(target.Health.Max); // snare doesn't block
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Dead Target / Dead Attacker
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Stops_when_target_dies()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2, maxHealth: 1); // will die on first hit
        target.Health.TakeDamage(1); // kill target

        var comp = MakeComponent(distance: FixedDistance(3f));
        attacker.Attach(comp);
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
        attacker.Health.TakeDamage(1); // kill attacker

        var comp = MakeComponent(distance: FixedDistance(3f));
        attacker.Attach(comp);
        comp.StartAttack(target);

        comp.Update(0);

        comp.IsAttacking.ShouldBeFalse();
        target.Health.Current.ShouldBe(target.Health.Max); // no damage dealt
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Facing Check
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Melee_swing_skipped_when_not_facing()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        var comp = MakeComponent(
            distance: FixedDistance(3f),
            facing: NeverFacing);
        attacker.Attach(comp);

        comp.StartAttack(target);
        comp.Update(0);

        target.Health.Current.ShouldBe(target.Health.Max); // no swing
        comp.IsAttacking.ShouldBeTrue(); // still trying
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Damage Context Flags
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Main_hand_context_has_auto_attack_flag()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        DamageContext? captured = null;

        var comp = MakeComponent(
            distance: FixedDistance(3f),
            random: FixedRandom(80)); // no offhand
        attacker.Attach(comp);
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
        DamageContext? lastCtx = null;

        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(), OffhandWeapon()),
            distance: FixedDistance(3f),
            random: FixedRandom(30)); // offhand procs
        attacker.Attach(comp);
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
        attacker.Stats.SetBase(StatId.BallisticSkill, 400);
        attacker.Stats.Flush();

        DamageContext? captured = null;
        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(), null, RangedWeapon()),
            distance: FixedDistance(50f),
            random: FixedRandom(80));
        attacker.Attach(comp);
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
        DamageContext? captured = null;

        // speed=300 â†’ CastTimeDamageMult = 300/100 = 3.0
        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(dps: 80, speed: 300)),
            distance: FixedDistance(3f),
            random: FixedRandom(80));
        attacker.Attach(comp);
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
        DamageContext? captured = null;

        var comp = MakeComponent(
            distance: FixedDistance(3f),
            random: FixedRandom(80));
        attacker.Attach(comp);
        comp.OnHit = (_, _, ctx) => captured = ctx;

        comp.StartAttack(target);
        comp.Update(0);

        captured.ShouldNotBeNull();
        captured.StatCoefficient.ShouldBe(0.1f);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Entity Integration (ITickable via WorldEntity.Update)
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Component_ticked_by_entity_update()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon()),
            distance: FixedDistance(3f));
        attacker.Attach(comp);

        comp.StartAttack(target);

        // Use entity update which ticks all ITickable components
        attacker.Update(0);

        target.Health.Current.ShouldBeLessThan(target.Health.Max);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  No Weapon Fallback
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void No_weapon_uses_default_speed()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        // No weapons at all â€” uses default weapon speed
        var comp = MakeComponent(
            weapons: SimpleWeapons(null, null, null),
            distance: FixedDistance(3f));
        attacker.Attach(comp);

        comp.StartAttack(target);
        comp.Update(0);

        // With null weapon DPS = 0, no damage but interval should use default (200)
        // weaponDps=0 â†’ WeaponDps=0 â†’ ctx.Damage = 0 â†’ FinalDamage = 0
        // Attack should still complete without error
        comp.IsAttacking.ShouldBeTrue();
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Offhand with OffhandDamage stat bonus
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Offhand_uses_lower_stat_coefficient()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        DamageContext? lastCtx = null;

        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(), OffhandWeapon()),
            distance: FixedDistance(3f),
            random: FixedRandom(30)); // offhand procs
        attacker.Attach(comp);
        comp.OnHit = (_, _, ctx) => lastCtx = ctx;

        comp.StartAttack(target);
        comp.Update(0);

        lastCtx.ShouldNotBeNull();
        lastCtx.StatCoefficient.ShouldBe(0.05f);
    }

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  Multiple Swings
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void Multiple_swings_accumulate_damage()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2, maxHealth: 100_000);
        attacker.Stats.SetBase(StatId.Wounds, 10_000);
        attacker.Stats.Flush();
        attacker.Health.Heal(100_000); // heal to full since Wounds changed max

        var comp = MakeComponent(
            weapons: SimpleWeapons(MeleeWeapon(dps: 80, speed: 200)),
            distance: FixedDistance(3f),
            random: FixedRandom(80)); // no offhand
        attacker.Attach(comp);

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

    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•
    //  OnHit callback
    // â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•â•

    [Fact]
    public void OnHit_fires_for_each_swing()
    {
        var attacker = MakeUnit(1);
        var target = MakeUnit(2);
        int hitCount = 0;

        var comp = MakeComponent(
            distance: FixedDistance(3f),
            random: FixedRandom(80)); // no offhand
        attacker.Attach(comp);
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
}
