using Core.Domain.Entities;
using Core.GameWorld.Combat;
using Core.GameWorld.Combat.Abilities;
using Core.GameWorld.Entities;
using Core.GameWorld.Stats;
using Shouldly;

namespace Core.GameWorld.Tests;

/// <summary>
/// Integration tests for Step 5: <see cref="AbilityCastService"/>,
/// <see cref="AbilityComponent"/>, and the end-to-end cast pipeline.
/// </summary>
public class AbilityCastServiceTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static PlayerEntity MakeUnit(ushort id = 1, uint maxHealth = 1000)
    {
        var entity = new PlayerEntity(id,
            new Character { CharacterId = id, Name = $"Unit{id}" }, maxHealth);
        entity.Level = 40;
        entity.ActionPoints = 250;
        return entity;
    }

    /// <summary>Creates a unit with an <see cref="AbilityComponent"/> already attached.</summary>
    private static (PlayerEntity entity, AbilityComponent comp) MakeCaster(
        ushort id = 1, uint maxHealth = 1000)
    {
        var entity = MakeUnit(id, maxHealth);
        var comp = new AbilityComponent();
        entity.Attach(comp);
        return (entity, comp);
    }

    /// <summary>Places an entity at the given region-absolute coordinates.</summary>
    private static void PlaceAt(WorldEntity entity, int x, int y) =>
        entity.Position = WorldPosition.FromRegionAbsolute(1, 1, x, y, 0, 0);

    private static AbilityDefinition MakeDef(
        ushort entry = 1000,
        ushort castTime = 0,
        ushort cooldown = 5000,
        byte apCost = 25,
        ushort range = 0,
        byte minRange = 0,
        AbilityType abilityType = AbilityType.Melee,
        CommandTargetType targetType = CommandTargetType.Enemy,
        bool ignoreGcd = false,
        bool ignoreOwnModifiers = false,
        bool affectsDead = false,
        ushort channelId = 0,
        ushort channelInterval = 0,
        byte fragile = 0,
        WeaponRequirement weaponNeeded = WeaponRequirement.None,
        ushort cooldownCap = 0,
        ushort cooldownEntry = 0,
        IReadOnlyList<AbilityCommandDefinition>? commands = null,
        IReadOnlyList<AbilityModifierDefinition>? modifiers = null)
    {
        return new AbilityDefinition
        {
            Entry = entry,
            Name = $"Ability{entry}",
            CastTime = castTime,
            Cooldown = cooldown,
            ApCost = apCost,
            Range = range,
            MinRange = minRange,
            AbilityType = abilityType,
            TargetType = targetType,
            IgnoreGlobalCooldown = ignoreGcd,
            IgnoreOwnModifiers = ignoreOwnModifiers,
            AffectsDead = affectsDead,
            ChannelId = channelId,
            ChannelInterval = channelInterval,
            Fragile = fragile,
            WeaponNeeded = weaponNeeded,
            CooldownCap = cooldownCap,
            CooldownEntry = cooldownEntry,
            Commands = commands ?? [],
            Modifiers = modifiers ?? [],
        };
    }

    /// <summary>
    /// Creates a DealDamage command with deterministic RawDamage output.
    /// RawDamage bypasses defense + armor. NoCrits avoids crit variance.
    /// MinDamage == MaxDamage + DamageVariance=0 eliminates randomness.
    /// </summary>
    private static AbilityCommandDefinition MakeDamageCmd(
        ushort baseDamage = 100,
        CommandTargetType targetType = CommandTargetType.Enemy)
    {
        return new AbilityCommandDefinition
        {
            EffectType = AbilityEffectType.DealDamage,
            TargetType = targetType,
            Damage = new DamageDefinition
            {
                MinDamage = baseDamage,
                MaxDamage = baseDamage,
                DamageVariance = 0,
                DamageType = DamageType.RawDamage,
                NoCrits = true,
                Undefendable = true,
            },
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AbilityComponent — Cooldowns & GCD
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Component_cooldown_tracks_expiry()
    {
        var comp = new AbilityComponent();
        comp.SetCooldown(100, tick: 1000, durationMs: 5000);

        comp.IsOnCooldown(100, tick: 1000).ShouldBeTrue();
        comp.IsOnCooldown(100, tick: 5999).ShouldBeTrue();
        comp.IsOnCooldown(100, tick: 6000).ShouldBeFalse();
        comp.GetCooldownExpiry(100).ShouldBe(6000);
    }

    [Fact]
    public void Component_gcd_tracks_expiry()
    {
        var comp = new AbilityComponent();
        comp.SetGlobalCooldown(tick: 1000);

        comp.IsOnGlobalCooldown(1000).ShouldBeTrue();
        comp.IsOnGlobalCooldown(2499).ShouldBeTrue();
        comp.IsOnGlobalCooldown(2500).ShouldBeFalse();
    }

    [Fact]
    public void Component_purge_expired_removes_old_cooldowns()
    {
        var comp = new AbilityComponent();
        comp.SetCooldown(100, tick: 0, durationMs: 1000);
        comp.SetCooldown(200, tick: 0, durationMs: 5000);

        comp.PurgeExpired(tick: 2000);

        comp.IsOnCooldown(100, tick: 0).ShouldBeFalse(); // expired, removed
        comp.IsOnCooldown(200, tick: 2000).ShouldBeTrue(); // still active
    }

    [Fact]
    public void Component_clear_gcd_resets()
    {
        var comp = new AbilityComponent();
        comp.SetGlobalCooldown(tick: 0);
        comp.ClearGlobalCooldown();
        comp.IsOnGlobalCooldown(0).ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  TryInitiate — Validation Failures
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Initiate_fails_when_caster_is_dead()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        caster.Health.TakeDamage(caster.Health.Max);

        var ctx = svc.TryInitiate(comp, MakeDef(), caster, null, 0, out var fail);

        ctx.ShouldBeNull();
        fail.ShouldBe(AbilityFailure.CasterDead);
    }

    [Fact]
    public void Initiate_fails_when_already_casting()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        comp.ActiveCast = new AbilityCastContext(MakeDef(), caster);

        var ctx = svc.TryInitiate(comp, MakeDef(entry: 2000), caster, null, 0, out var fail);

        ctx.ShouldBeNull();
        fail.ShouldBe(AbilityFailure.AlreadyActive);
    }

    [Fact]
    public void Initiate_fails_when_on_gcd()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        comp.SetGlobalCooldown(0);

        var ctx = svc.TryInitiate(comp, MakeDef(), caster, null, 500, out var fail);

        ctx.ShouldBeNull();
        fail.ShouldBe(AbilityFailure.Cooldown);
    }

    [Fact]
    public void Initiate_succeeds_when_gcd_ignored()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        comp.SetGlobalCooldown(0);

        var def = MakeDef(ignoreGcd: true, targetType: CommandTargetType.Caster);
        var ctx = svc.TryInitiate(comp, def, caster, null, 500, out var fail);

        ctx.ShouldNotBeNull();
        fail.ShouldBe(AbilityFailure.Ok);
    }

    [Fact]
    public void Initiate_fails_when_on_cooldown()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        comp.SetCooldown(1000, tick: 0, durationMs: 10_000);

        var ctx = svc.TryInitiate(comp, MakeDef(), caster, null, 5000, out var fail);

        ctx.ShouldBeNull();
        fail.ShouldBe(AbilityFailure.Cooldown);
    }

    [Fact]
    public void Initiate_fails_when_not_enough_ap()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        caster.ActionPoints = 10;

        var ctx = svc.TryInitiate(comp, MakeDef(apCost: 25), caster, null, 0, out var fail);

        ctx.ShouldBeNull();
        fail.ShouldBe(AbilityFailure.NotEnoughAp);
    }

    [Fact]
    public void Initiate_fails_when_target_dead_and_not_affects_dead()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);
        target.Health.TakeDamage(target.Health.Max);

        var ctx = svc.TryInitiate(comp, MakeDef(), caster, target, 0, out var fail);

        ctx.ShouldBeNull();
        fail.ShouldBe(AbilityFailure.TargetDead);
    }

    [Fact]
    public void Initiate_fails_when_target_alive_and_affects_dead()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);

        var ctx = svc.TryInitiate(
            comp, MakeDef(affectsDead: true), caster, target, 0, out var fail);

        ctx.ShouldBeNull();
        fail.ShouldBe(AbilityFailure.InvalidTarget);
    }

    [Fact]
    public void Initiate_fails_when_no_target_and_requires_target()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();

        var ctx = svc.TryInitiate(
            comp, MakeDef(targetType: CommandTargetType.Enemy), caster, null, 0, out var fail);

        ctx.ShouldBeNull();
        fail.ShouldBe(AbilityFailure.InvalidTarget);
    }

    [Fact]
    public void Initiate_fails_when_out_of_range()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);
        // 100 feet = 1200 units; place 1800 units apart (150 feet)
        PlaceAt(caster, 1000, 1000);
        PlaceAt(target, 2800, 1000);

        var ctx = svc.TryInitiate(
            comp, MakeDef(range: 100), caster, target, 0, out var fail);

        ctx.ShouldBeNull();
        fail.ShouldBe(AbilityFailure.OutOfRange);
    }

    [Fact]
    public void Initiate_fails_when_too_close()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);
        // MinRange = 20 feet = 240 units; place 120 units apart (10 feet)
        PlaceAt(caster, 1000, 1000);
        PlaceAt(target, 1120, 1000);

        var ctx = svc.TryInitiate(
            comp, MakeDef(range: 100, minRange: 20), caster, target, 0, out var fail);

        ctx.ShouldBeNull();
        fail.ShouldBe(AbilityFailure.TooClose);
    }

    [Fact]
    public void Initiate_succeeds_when_in_range()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);
        // 50 feet = 600 units; range 100 feet = 1200 units
        PlaceAt(caster, 1000, 1000);
        PlaceAt(target, 1600, 1000);

        var ctx = svc.TryInitiate(
            comp, MakeDef(range: 100), caster, target, 0, out var fail);

        ctx.ShouldNotBeNull();
        fail.ShouldBe(AbilityFailure.Ok);
    }

    [Fact]
    public void Initiate_fails_when_wrong_weapon()
    {
        var svc = new AbilityCastService();
        svc.WeaponCheck = (_, _) => false; // always fail
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);
        PlaceAt(caster, 0, 0);
        PlaceAt(target, 0, 0);

        var ctx = svc.TryInitiate(
            comp,
            MakeDef(weaponNeeded: WeaponRequirement.Shield),
            caster, target, 0, out var fail);

        ctx.ShouldBeNull();
        fail.ShouldBe(AbilityFailure.WrongWeapon);
    }

    [Fact]
    public void Initiate_fails_when_silenced_and_verbal()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        // Apply a silence buff
        caster.Buffs.QueueBuff(
            new BuffDefinition
            {
                Entry = 9999,
                Name = "Silence",
                BuffClass = BuffClass.Buff0,
                DurationMs = 10_000,
                CrowdControl = CrowdControlFlags.Silence,
            },
            caster);
        caster.Buffs.Update(0);

        var ctx = svc.TryInitiate(
            comp,
            MakeDef(abilityType: AbilityType.Verbal, targetType: CommandTargetType.Caster),
            caster, null, 0, out var fail);

        ctx.ShouldBeNull();
        fail.ShouldBe(AbilityFailure.Silenced);
    }

    [Fact]
    public void Initiate_fails_when_disarmed_and_melee()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        caster.Buffs.QueueBuff(
            new BuffDefinition
            {
                Entry = 9998,
                Name = "Disarm",
                BuffClass = BuffClass.Buff0,
                DurationMs = 10_000,
                CrowdControl = CrowdControlFlags.Disarm,
            },
            caster);
        caster.Buffs.Update(0);

        var ctx = svc.TryInitiate(
            comp,
            MakeDef(abilityType: AbilityType.Melee, targetType: CommandTargetType.Caster),
            caster, null, 0, out var fail);

        ctx.ShouldBeNull();
        fail.ShouldBe(AbilityFailure.Disarmed);
    }

    [Fact]
    public void Initiate_fails_when_knockdown()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        caster.Buffs.QueueBuff(
            new BuffDefinition
            {
                Entry = 9997,
                Name = "Knockdown",
                BuffClass = BuffClass.Buff0,
                DurationMs = 10_000,
                CrowdControl = CrowdControlFlags.Knockdown,
            },
            caster);
        caster.Buffs.Update(0);

        var ctx = svc.TryInitiate(
            comp,
            MakeDef(targetType: CommandTargetType.Caster),
            caster, null, 0, out var fail);

        ctx.ShouldBeNull();
        fail.ShouldBe(AbilityFailure.Knockdown);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Instant Cast — Integration
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Instant_cast_deals_damage_and_consumes_ap()
    {
        var svc = new AbilityCastService(new Random(42));
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);
        PlaceAt(caster, 0, 0);
        PlaceAt(target, 0, 0);

        var def = MakeDef(apCost: 25, commands: [MakeDamageCmd(100)]);
        var ctx = svc.TryInitiate(comp, def, caster, target, 0, out _);
        ctx.ShouldNotBeNull();

        svc.ConfirmCast(comp, ctx, 0).ShouldBeTrue();

        target.Health.Current.ShouldBe(900u); // 1000 − 100
        caster.ActionPoints.ShouldBe(225);    // 250 − 25
        comp.HasActiveCast.ShouldBeFalse();   // cleared after instant
    }

    [Fact]
    public void Instant_cast_sets_gcd()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        PlaceAt(caster, 0, 0);

        var def = MakeDef(targetType: CommandTargetType.Caster);
        var ctx = svc.TryInitiate(comp, def, caster, null, 1000, out _)!;
        svc.ConfirmCast(comp, ctx, 1000);

        comp.IsOnGlobalCooldown(1000).ShouldBeTrue();
        comp.IsOnGlobalCooldown(2500).ShouldBeFalse();
    }

    [Fact]
    public void Instant_cast_sets_cooldown()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        PlaceAt(caster, 0, 0);

        var def = MakeDef(entry: 1000, cooldown: 5000, targetType: CommandTargetType.Caster);
        var ctx = svc.TryInitiate(comp, def, caster, null, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        comp.IsOnCooldown(1000, tick: 0).ShouldBeTrue();
        comp.IsOnCooldown(1000, tick: 4999).ShouldBeTrue();
        comp.IsOnCooldown(1000, tick: 5000).ShouldBeFalse();
    }

    [Fact]
    public void Instant_cast_uses_shared_cooldown_entry()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        PlaceAt(caster, 0, 0);

        var def = MakeDef(
            entry: 1000, cooldown: 5000, cooldownEntry: 500,
            targetType: CommandTargetType.Caster);
        var ctx = svc.TryInitiate(comp, def, caster, null, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        comp.IsOnCooldown(500, tick: 0).ShouldBeTrue(); // shared entry
        comp.IsOnCooldown(1000, tick: 0).ShouldBeFalse(); // own entry not set
    }

    [Fact]
    public void Instant_cast_zero_cooldown_sets_none()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        PlaceAt(caster, 0, 0);

        var def = MakeDef(cooldown: 0, targetType: CommandTargetType.Caster);
        var ctx = svc.TryInitiate(comp, def, caster, null, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        comp.IsOnCooldown(1000, tick: 0).ShouldBeFalse();
    }

    [Fact]
    public void Instant_steal_life_heals_caster()
    {
        var svc = new AbilityCastService(new Random(42));
        var (caster, comp) = MakeCaster();
        caster.Health.TakeDamage(500); // caster at 500/1000
        var target = MakeUnit(2);
        PlaceAt(caster, 0, 0);
        PlaceAt(target, 0, 0);

        var cmd = new AbilityCommandDefinition
        {
            EffectType = AbilityEffectType.StealLife,
            TargetType = CommandTargetType.Enemy,
            Damage = new DamageDefinition
            {
                MinDamage = 100,
                MaxDamage = 100,
                DamageType = DamageType.RawDamage,
                NoCrits = true,
                Undefendable = true,
            },
        };
        var def = MakeDef(apCost: 0, commands: [cmd]);
        var ctx = svc.TryInitiate(comp, def, caster, target, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        target.Health.Current.ShouldBe(900u);
        caster.Health.Current.ShouldBe(600u); // 500 + 100 healed
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Cast Bar (CastTime > 0)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CastBar_registers_pending_and_completes_on_tick()
    {
        var svc = new AbilityCastService(new Random(42));
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);
        PlaceAt(caster, 0, 0);
        PlaceAt(target, 0, 0);

        var def = MakeDef(castTime: 2000, apCost: 25, commands: [MakeDamageCmd(100)]);
        var ctx = svc.TryInitiate(comp, def, caster, target, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        // Cast bar active, damage not applied yet
        comp.HasActiveCast.ShouldBeTrue();
        target.Health.Current.ShouldBe(1000u);
        caster.ActionPoints.ShouldBe(250); // AP not consumed yet

        // Tick at 50% — still casting
        svc.Update(comp, caster, 1000);
        comp.HasActiveCast.ShouldBeTrue();

        // Tick at 100% — cast completes
        svc.Update(comp, caster, 2000);
        comp.HasActiveCast.ShouldBeFalse();
        target.Health.Current.ShouldBe(900u);
        caster.ActionPoints.ShouldBe(225); // AP consumed on completion
    }

    [Fact]
    public void CastBar_60_percent_range_check_cancels_if_target_moved()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);
        PlaceAt(caster, 0, 0);
        PlaceAt(target, 0, 0);

        var def = MakeDef(castTime: 2000, range: 100, commands: [MakeDamageCmd(100)]);
        var ctx = svc.TryInitiate(comp, def, caster, target, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        // Move target out of range before the 60% mark
        PlaceAt(target, 100_000, 0);

        // Tick past 60% (1200ms)
        svc.Update(comp, caster, 1300);
        comp.HasActiveCast.ShouldBeFalse(); // cancelled
    }

    [Fact]
    public void CastBar_clears_gcd_when_interrupted()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        PlaceAt(caster, 0, 0);

        var def = MakeDef(
            castTime: 2000, targetType: CommandTargetType.Caster);
        var ctx = svc.TryInitiate(comp, def, caster, null, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        comp.IsOnGlobalCooldown(0).ShouldBeTrue();
        svc.CancelCast(comp, AbilityFailure.Interrupted);
        comp.IsOnGlobalCooldown(0).ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Channeling
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Channel_applies_effects_per_tick()
    {
        var svc = new AbilityCastService(new Random(42));
        var (caster, comp) = MakeCaster();
        caster.ActionPoints = 500; // enough for several ticks
        var target = MakeUnit(2, 10_000);
        PlaceAt(caster, 0, 0);
        PlaceAt(target, 0, 0);

        var def = MakeDef(
            castTime: 3000, channelId: 1, channelInterval: 1000,
            apCost: 10, commands: [MakeDamageCmd(100)]);
        var ctx = svc.TryInitiate(comp, def, caster, target, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        comp.HasActiveCast.ShouldBeTrue();
        target.Health.Current.ShouldBe(10_000u); // no tick yet

        // First tick at 1000ms
        svc.Update(comp, caster, 1000);
        target.Health.Current.ShouldBe(9_900u);     // −100 from first tick
        caster.ActionPoints.ShouldBe(490);          // −10 AP per tick

        // Second tick at 2000ms
        svc.Update(comp, caster, 2000);
        target.Health.Current.ShouldBe(9_800u);
        caster.ActionPoints.ShouldBe(480);

        // Channel ends at 3000ms
        svc.Update(comp, caster, 3000);
        comp.HasActiveCast.ShouldBeFalse();
    }

    [Fact]
    public void Channel_cancels_when_target_dies()
    {
        var svc = new AbilityCastService(new Random(42));
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2, maxHealth: 50); // low HP
        PlaceAt(caster, 0, 0);
        PlaceAt(target, 0, 0);

        var def = MakeDef(
            castTime: 5000, channelId: 1, channelInterval: 1000,
            apCost: 0, commands: [MakeDamageCmd(100)]);
        var ctx = svc.TryInitiate(comp, def, caster, target, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        // First tick kills the target (100 > 50)
        svc.Update(comp, caster, 1000);
        target.Health.IsDead.ShouldBeTrue();

        // Next tick should cancel the channel
        svc.Update(comp, caster, 2000);
        comp.HasActiveCast.ShouldBeFalse();
    }

    [Fact]
    public void Channel_cancels_when_ap_runs_out()
    {
        var svc = new AbilityCastService(new Random(42));
        var (caster, comp) = MakeCaster();
        caster.ActionPoints = 15;
        var target = MakeUnit(2, 10_000);
        PlaceAt(caster, 0, 0);
        PlaceAt(target, 0, 0);

        var def = MakeDef(
            castTime: 5000, channelId: 1, channelInterval: 1000,
            apCost: 10, commands: [MakeDamageCmd(100)]);
        var ctx = svc.TryInitiate(comp, def, caster, target, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        // First tick: 15 ≥ 10 → OK
        svc.Update(comp, caster, 1000);
        caster.ActionPoints.ShouldBe(5);

        // Second tick: 5 < 10 → cancel
        svc.Update(comp, caster, 2000);
        comp.HasActiveCast.ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Setback & Fragile
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Setback_extends_cast_time()
    {
        var svc = new AbilityCastService(new Random(42));
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);
        PlaceAt(caster, 0, 0);
        PlaceAt(target, 0, 0);

        var def = MakeDef(castTime: 2000, commands: [MakeDamageCmd(100)]);
        var ctx = svc.TryInitiate(comp, def, caster, target, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        // Add 500ms of setback
        svc.AddSetback(comp, 500);
        ctx.SetbackAccumulator.ShouldBe(500f);

        // Tick at 2000ms — would normally complete, but now needs 2500ms
        svc.Update(comp, caster, 2000);
        comp.HasActiveCast.ShouldBeTrue();

        // Tick at 2500ms — now completes
        svc.Update(comp, caster, 2500);
        comp.HasActiveCast.ShouldBeFalse();
        target.Health.Current.ShouldBe(900u);
    }

    [Fact]
    public void Setback_fragile_2_interrupts_immediately()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);
        PlaceAt(caster, 0, 0);
        PlaceAt(target, 0, 0);

        var def = MakeDef(castTime: 2000, fragile: 2, commands: [MakeDamageCmd(100)]);
        var ctx = svc.TryInitiate(comp, def, caster, target, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        svc.AddSetback(comp, 100);
        comp.HasActiveCast.ShouldBeFalse();
        target.Health.Current.ShouldBe(1000u); // no damage dealt
    }

    [Fact]
    public void Setback_ignored_for_instant_casts()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        // Instant cast has no active cast after confirm
        svc.AddSetback(comp, 100); // should not crash
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ConfirmCast — Re-validation
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ConfirmCast_fails_if_target_died_since_initiation()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);
        PlaceAt(caster, 0, 0);
        PlaceAt(target, 0, 0);

        var ctx = svc.TryInitiate(comp, MakeDef(), caster, target, 0, out _)!;

        // Target dies between initiation and confirm
        target.Health.TakeDamage(target.Health.Max);

        svc.ConfirmCast(comp, ctx, 50).ShouldBeFalse();
        ctx.FailureCode.ShouldBe(AbilityFailure.TargetDead);
    }

    [Fact]
    public void ConfirmCast_fails_if_target_moved_out_of_range()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);
        PlaceAt(caster, 0, 0);
        PlaceAt(target, 0, 0);

        var ctx = svc.TryInitiate(comp, MakeDef(range: 100), caster, target, 0, out _)!;

        PlaceAt(target, 100_000, 0); // move far away

        svc.ConfirmCast(comp, ctx, 50).ShouldBeFalse();
        ctx.FailureCode.ShouldBe(AbilityFailure.OutOfRange);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Modifier Pipeline
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PreCast_modifier_reduces_ap_cost()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        caster.ActionPoints = 100;
        PlaceAt(caster, 0, 0);

        var def = MakeDef(
            apCost: 50,
            targetType: CommandTargetType.Caster,
            modifiers:
            [
                new AbilityModifierDefinition
                {
                    Stage = ModifierStage.PreCast,
                    Operation = ModifierOperation.SetApCost,
                    Value = 10,
                },
            ]);

        var ctx = svc.TryInitiate(comp, def, caster, null, 0, out _)!;
        ctx.ApCost.ShouldBe(10f); // modified from 50 to 10

        svc.ConfirmCast(comp, ctx, 0);
        caster.ActionPoints.ShouldBe(90); // 100 − 10 (not 50)
    }

    [Fact]
    public void PreCast_modifiers_skipped_when_ignoreOwnModifiers()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        PlaceAt(caster, 0, 0);

        var def = MakeDef(
            apCost: 50,
            targetType: CommandTargetType.Caster,
            ignoreOwnModifiers: true,
            modifiers:
            [
                new AbilityModifierDefinition
                {
                    Stage = ModifierStage.PreCast,
                    Operation = ModifierOperation.SetApCost,
                    Value = 10,
                },
            ]);

        var ctx = svc.TryInitiate(comp, def, caster, null, 0, out _)!;
        ctx.ApCost.ShouldBe(50f); // not modified
    }

    [Fact]
    public void PostCast_modifier_adjusts_damage_bonus()
    {
        var svc = new AbilityCastService(new Random(42));
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);
        PlaceAt(caster, 0, 0);
        PlaceAt(target, 0, 0);

        var def = MakeDef(
            apCost: 0,
            commands: [MakeDamageCmd(100)],
            modifiers:
            [
                new AbilityModifierDefinition
                {
                    Stage = ModifierStage.PostCast,
                    Operation = ModifierOperation.MultiplyDamageBonus,
                    Value = 2f,
                },
            ]);

        var ctx = svc.TryInitiate(comp, def, caster, target, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        // DamageBonus goes from 1.0 to 2.0, so raw damage 100 → pipeline applies bonus → 200
        target.Health.Current.ShouldBe(800u);
    }

    [Fact]
    public void Conditional_modifier_applied_when_evaluator_returns_true()
    {
        var svc = new AbilityCastService();
        svc.ConditionEvaluator = (cond, val, ctx) => true; // always met
        var (caster, comp) = MakeCaster();
        PlaceAt(caster, 0, 0);

        var def = MakeDef(
            apCost: 50,
            targetType: CommandTargetType.Caster,
            modifiers:
            [
                new AbilityModifierDefinition
                {
                    Stage = ModifierStage.PreCast,
                    Operation = ModifierOperation.SetApCost,
                    Value = 0,
                    Condition = ModifierCondition.HasBuff,
                    ConditionValue = 999,
                },
            ]);

        var ctx = svc.TryInitiate(comp, def, caster, null, 0, out _)!;
        ctx.ApCost.ShouldBe(0f); // condition met, modifier applied
    }

    [Fact]
    public void Conditional_modifier_skipped_when_evaluator_returns_false()
    {
        var svc = new AbilityCastService();
        svc.ConditionEvaluator = (cond, val, ctx) => false; // never met
        var (caster, comp) = MakeCaster();
        PlaceAt(caster, 0, 0);

        var def = MakeDef(
            apCost: 50,
            targetType: CommandTargetType.Caster,
            modifiers:
            [
                new AbilityModifierDefinition
                {
                    Stage = ModifierStage.PreCast,
                    Operation = ModifierOperation.SetApCost,
                    Value = 0,
                    Condition = ModifierCondition.HasBuff,
                    ConditionValue = 999,
                },
            ]);

        var ctx = svc.TryInitiate(comp, def, caster, null, 0, out _)!;
        ctx.ApCost.ShouldBe(50f); // condition not met, modifier skipped
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Cooldown Cap
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Cooldown_respects_cooldown_cap()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        PlaceAt(caster, 0, 0);

        // Cooldown 5000, modifier reduces to 2000, cap is 3000
        var def = MakeDef(
            entry: 1000, cooldown: 5000, cooldownCap: 3000,
            targetType: CommandTargetType.Caster,
            modifiers:
            [
                new AbilityModifierDefinition
                {
                    Stage = ModifierStage.PreCast,
                    Operation = ModifierOperation.SetCooldown,
                    Value = 2000,
                },
            ]);

        var ctx = svc.TryInitiate(comp, def, caster, null, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        // Cap enforces minimum cooldown of 3000ms
        comp.IsOnCooldown(1000, tick: 2999).ShouldBeTrue();
        comp.IsOnCooldown(1000, tick: 3000).ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ModifyActionPoints Effect
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ModifyActionPoints_adds_ap_to_target()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        caster.ActionPoints = 100;
        PlaceAt(caster, 0, 0);

        var cmd = new AbilityCommandDefinition
        {
            EffectType = AbilityEffectType.ModifyActionPoints,
            TargetType = CommandTargetType.Caster,
            PrimaryValue = 50,
        };
        var def = MakeDef(apCost: 0, targetType: CommandTargetType.Caster, commands: [cmd]);
        var ctx = svc.TryInitiate(comp, def, caster, null, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        caster.ActionPoints.ShouldBe(150);
    }

    [Fact]
    public void ModifyActionPoints_does_not_go_below_zero()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        caster.ActionPoints = 30;
        PlaceAt(caster, 0, 0);

        var cmd = new AbilityCommandDefinition
        {
            EffectType = AbilityEffectType.ModifyActionPoints,
            TargetType = CommandTargetType.Caster,
            PrimaryValue = -100,
        };
        var def = MakeDef(apCost: 0, targetType: CommandTargetType.Caster, commands: [cmd]);
        var ctx = svc.TryInitiate(comp, def, caster, null, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        caster.ActionPoints.ShouldBe(0);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  NoAutoUse Commands
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void NoAutoUse_commands_are_skipped()
    {
        var svc = new AbilityCastService(new Random(42));
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);
        PlaceAt(caster, 0, 0);
        PlaceAt(target, 0, 0);

        var cmd = new AbilityCommandDefinition
        {
            EffectType = AbilityEffectType.DealDamage,
            TargetType = CommandTargetType.Enemy,
            NoAutoUse = true,
            Damage = new DamageDefinition
            {
                MinDamage = 100, MaxDamage = 100,
                DamageType = DamageType.RawDamage, NoCrits = true, Undefendable = true,
            },
        };
        var def = MakeDef(apCost: 0, commands: [cmd]);
        var ctx = svc.TryInitiate(comp, def, caster, target, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        target.Health.Current.ShouldBe(1000u); // no damage — command was skipped
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Self-Targeted Abilities
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Caster_targeted_command_resolves_to_self()
    {
        var svc = new AbilityCastService(new Random(42));
        var (caster, comp) = MakeCaster();
        caster.Health.TakeDamage(200); // 800/1000
        PlaceAt(caster, 0, 0);

        var cmd = new AbilityCommandDefinition
        {
            EffectType = AbilityEffectType.DealDamage,
            TargetType = CommandTargetType.Caster,
            Damage = new DamageDefinition
            {
                MinDamage = 50, MaxDamage = 50,
                DamageType = DamageType.RawDamage, NoCrits = true, Undefendable = true,
            },
        };
        var def = MakeDef(apCost: 0, targetType: CommandTargetType.Caster, commands: [cmd]);
        var ctx = svc.TryInitiate(comp, def, caster, null, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        caster.Health.Current.ShouldBe(750u); // 800 − 50
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Cancel Cast
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CancelCast_clears_active_cast()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);
        PlaceAt(caster, 0, 0);
        PlaceAt(target, 0, 0);

        var def = MakeDef(castTime: 2000, commands: [MakeDamageCmd(100)]);
        var ctx = svc.TryInitiate(comp, def, caster, target, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        svc.CancelCast(comp, AbilityFailure.Cancelled);

        comp.HasActiveCast.ShouldBeFalse();
        target.Health.Current.ShouldBe(1000u); // no damage
    }

    [Fact]
    public void CancelCast_on_idle_component_is_noop()
    {
        var svc = new AbilityCastService();
        var comp = new AbilityComponent();
        svc.CancelCast(comp, AbilityFailure.Cancelled); // should not throw
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Update — Idle and Failed
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Update_does_nothing_when_no_active_cast()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        svc.Update(comp, caster, 1000); // should not throw
    }

    [Fact]
    public void Update_clears_failed_cast()
    {
        var svc = new AbilityCastService();
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);
        PlaceAt(caster, 0, 0);
        PlaceAt(target, 0, 0);

        var def = MakeDef(castTime: 2000, commands: [MakeDamageCmd(100)]);
        var ctx = svc.TryInitiate(comp, def, caster, target, 0, out _)!;
        svc.ConfirmCast(comp, ctx, 0);

        ctx.Fail(AbilityFailure.Interrupted);
        svc.Update(comp, caster, 100);

        comp.HasActiveCast.ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Multiple Casts in Sequence
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Multiple_instant_casts_accumulate_damage()
    {
        var svc = new AbilityCastService(new Random(42));
        var (caster, comp) = MakeCaster();
        var target = MakeUnit(2);
        PlaceAt(caster, 0, 0);
        PlaceAt(target, 0, 0);

        var def = MakeDef(
            apCost: 0, cooldown: 0, ignoreGcd: true,
            commands: [MakeDamageCmd(100)]);

        for (var i = 0; i < 5; i++)
        {
            var ctx = svc.TryInitiate(comp, def, caster, target, i * 100, out _)!;
            svc.ConfirmCast(comp, ctx, i * 100);
        }

        target.Health.Current.ShouldBe(500u); // 1000 − (5 × 100)
    }
}
