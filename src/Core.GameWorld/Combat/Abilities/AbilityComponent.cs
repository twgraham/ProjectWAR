using Core.GameWorld.Combat.Career;
using Core.GameWorld.Entities;
using Core.GameWorld.Spatial;

namespace Core.GameWorld.Combat.Abilities;

/// <summary>
/// Per-entity ability state and lifecycle: tracks the active cast, cooldowns,
/// global cooldown, and drives the cast-bar / channel update loop.
/// <para>
/// Follows the split initiation / execution model (§11.6):
/// <list type="bullet">
///   <item><see cref="TryInitiate"/> — handler thread (read-only validation, context creation)</item>
///   <item><see cref="ConfirmCast"/> — region thread (re-validation, register pending, instant execution)</item>
///   <item><see cref="Update"/> — region thread tick (cast-bar timer, channel ticks, event emission)</item>
/// </list>
/// </para>
/// <para>
/// This is a direct field on <see cref="UnitEntity"/> — not in the optional component bag.
/// Effect execution is delegated to <see cref="AbilityEffectExecutor"/> to keep this
/// class focused on lifecycle management.
/// </para>
/// </summary>
public sealed class AbilityComponent
{
    private readonly Dictionary<ushort, long> _cooldowns = new();
    private long _globalCooldownExpiry;
    private readonly UnitEntity _owner;

    /// <summary>Default global cooldown duration in milliseconds.</summary>
    public const int DefaultGcdMs = 1500;

    /// <summary>
    /// Injectable weapon check. Returns <c>true</c> if the caster meets the weapon
    /// requirement. Defaults to always-pass (equipment system wired later).
    /// </summary>
    public Func<UnitEntity, WeaponRequirement, bool> WeaponCheck { get; set; } = (_, _) => true;

    /// <summary>
    /// Injectable condition evaluator for ability modifiers.
    /// If null, conditional modifiers are skipped.
    /// </summary>
    public Func<ModifierCondition, int, AbilityCastContext, bool>? ConditionEvaluator { get; set; }

    /// <summary>
    /// Effect executor for command resolution and damage pipeline.
    /// Defaults to <see cref="UnitEntity.SharedEffectExecutor"/>; override per-entity
    /// for custom behavior (e.g. deterministic tests).
    /// </summary>
    public AbilityEffectExecutor? EffectExecutor { get; set; }

    // ── Domain Callbacks ────────────────────────────────────────────────
    // The owning UnitEntity subscribes to these and translates them into
    // region-level tick events via its protected Emit() method.

    /// <summary>Invoked when a cast is confirmed (instant = already done, cast-bar = just started).</summary>
    public Action<AbilityCastContext>? OnCastConfirmed;

    /// <summary>Invoked when a cast-bar or channeled ability completes.</summary>
    public Action<AbilityCastContext>? OnCastCompleted;

    /// <summary>Invoked when a cast is cancelled or fails.</summary>
    public Action<AbilityCastContext, AbilityFailure>? OnCastFailed;

    /// <summary>Invoked when a cooldown is applied after cast completion.</summary>
    public Action<ushort, int>? OnCooldownApplied;

    /// <summary>
    /// Invoked when a damage effect is resolved. Caster and result are provided
    /// so the entity can emit a <c>DamageDealt</c> tick event.
    /// </summary>
    public Action<UnitEntity, DamageResult>? OnDamageDealt;

    public AbilityComponent(UnitEntity owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ACTIVE CAST STATE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>The in-progress cast, or null if idle.</summary>
    public AbilityCastContext? ActiveCast { get; internal set; }

    /// <summary>True if a cast is in progress.</summary>
    public bool HasActiveCast => ActiveCast is not null;

    /// <summary>Next channel tick timestamp. Valid only when channeling.</summary>
    internal long NextChannelTick { get; set; }

    /// <summary>Whether the 60% range re-check has fired for the current cast.</summary>
    internal bool RangeCheckDone { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    //  GLOBAL COOLDOWN
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>True if the GCD has not yet expired.</summary>
    public bool IsOnGlobalCooldown(long tick) => tick < _globalCooldownExpiry;

    /// <summary>Start (or overwrite) the GCD from the current tick.</summary>
    public void SetGlobalCooldown(long tick, int durationMs = DefaultGcdMs)
    {
        _globalCooldownExpiry = tick + durationMs;
    }

    /// <summary>Immediately clear the GCD (used when a cast-bar ability is interrupted).</summary>
    public void ClearGlobalCooldown() => _globalCooldownExpiry = 0;

    // ═══════════════════════════════════════════════════════════════════
    //  PER-ABILITY COOLDOWNS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>True if the entry's cooldown has not yet expired.</summary>
    public bool IsOnCooldown(ushort entry, long tick)
    {
        return _cooldowns.TryGetValue(entry, out var expiry) && tick < expiry;
    }

    /// <summary>Start a cooldown for the given entry.</summary>
    public void SetCooldown(ushort entry, long tick, int durationMs)
    {
        _cooldowns[entry] = tick + durationMs;
    }

    /// <summary>Returns the expiry tick for an entry, or 0 if none set.</summary>
    public long GetCooldownExpiry(ushort entry)
    {
        return _cooldowns.TryGetValue(entry, out var expiry) ? expiry : 0;
    }

    /// <summary>Remove all expired cooldowns to free dictionary memory.</summary>
    public void PurgeExpired(long tick)
    {
        List<ushort>? expired = null;
        foreach (var (entry, expiry) in _cooldowns)
        {
            if (tick >= expiry)
            {
                expired ??= [];
                expired.Add(entry);
            }
        }

        if (expired is not null)
            foreach (var entry in expired)
                _cooldowns.Remove(entry);
    }

    /// <summary>Clears active cast state.</summary>
    internal void ClearCast()
    {
        ActiveCast = null;
        NextChannelTick = 0;
        RangeCheckDone = false;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PHASE 1: INITIATION (handler-thread-safe, read-only)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validate the cast attempt and create an <see cref="AbilityCastContext"/>.
    /// <para>
    /// Read-only over entity state — all mutations go into the new context.
    /// On success the caller sends the cast-bar packet and enqueues
    /// <see cref="ConfirmCast"/> to the region thread.
    /// </para>
    /// </summary>
    /// <param name="definition">The ability to cast.</param>
    /// <param name="target">Primary target, or null for self-cast / ground-targeted.</param>
    /// <param name="tick">Current timestamp in ms.</param>
    /// <param name="failureCode">Set to the rejection reason on failure.</param>
    /// <returns>The cast context on success; <c>null</c> if validation failed.</returns>
    public AbilityCastContext? TryInitiate(
        AbilityDefinition definition,
        UnitEntity? target,
        long tick,
        out AbilityFailure failureCode)
    {
        failureCode = AbilityFailure.Ok;

        // 1. Caster alive
        if (_owner.Health.IsDead)
        {
            failureCode = AbilityFailure.CasterDead;
            return null;
        }

        // 2. Already casting
        if (HasActiveCast)
        {
            failureCode = AbilityFailure.AlreadyActive;
            return null;
        }

        // 3. Create context (snapshots definition defaults)
        var context = new AbilityCastContext(definition, _owner, target);

        // 4. Apply pre-cast modifiers (modify context only, not entity state)
        if (!definition.IgnoreOwnModifiers)
            ApplyModifiers(context, ModifierStage.PreCast);

        // 5. CC check
        var ccFailure = CheckCrowdControl(_owner, definition);
        if (ccFailure.HasValue)
        {
            failureCode = ccFailure.Value;
            return null;
        }

        // 6. GCD check
        if (!definition.IgnoreGlobalCooldown && IsOnGlobalCooldown(tick))
        {
            failureCode = AbilityFailure.Cooldown;
            return null;
        }

        // 7. Cooldown check (shared cooldown group or own entry)
        var cdEntry = definition.CooldownEntry != 0 ? definition.CooldownEntry : definition.Entry;
        if (IsOnCooldown(cdEntry, tick))
        {
            failureCode = AbilityFailure.Cooldown;
            return null;
        }

        // 8. AP check (advisory read — actual consumption at cast completion)
        if (context.ApCost > 0 && _owner.ActionPoints < (int)context.ApCost)
        {
            failureCode = AbilityFailure.NotEnoughAp;
            return null;
        }

        // 8b. Career resource check
        if (context.SpecialCost > 0)
        {
            var resource = _owner.TryGet<CareerResourceComponent>()?.Resource;
            if (resource is null || !resource.HasResource((int)context.SpecialCost))
            {
                failureCode = AbilityFailure.NotEnoughResource;
                return null;
            }
        }

        // 9. Target validation
        if (target is not null)
        {
            if (!definition.AffectsDead && target.Health.IsDead)
            {
                failureCode = AbilityFailure.TargetDead;
                return null;
            }

            if (definition.AffectsDead && target.Health.IsAlive)
            {
                failureCode = AbilityFailure.InvalidTarget;
                return null;
            }
        }
        else if (RequiresTarget(definition))
        {
            failureCode = AbilityFailure.InvalidTarget;
            return null;
        }

        // 10. Range check
        if (target is not null && context.Range > 0)
        {
            var rangeResult = CheckRange(_owner, target, context.Range, context.MinRange);
            if (rangeResult.HasValue)
            {
                failureCode = rangeResult.Value;
                return null;
            }
        }

        // 11. Weapon check (delegated — equipment system not yet wired)
        if (definition.WeaponNeeded != WeaponRequirement.None
            && !WeaponCheck(_owner, definition.WeaponNeeded))
        {
            failureCode = AbilityFailure.WrongWeapon;
            return null;
        }

        return context;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PHASE 2: CONFIRM CAST (region thread — mutations begin)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Region thread: re-validate and start the cast. State may have changed since
    /// <see cref="TryInitiate"/>. For instant casts, executes immediately.
    /// </summary>
    /// <returns><c>true</c> if the cast was confirmed.</returns>
    public bool ConfirmCast(AbilityCastContext context, long tick)
    {
        var target = context.Target;
        var definition = context.Definition;

        // Re-validate (state may have changed between initiation and region drain)
        if (_owner.Health.IsDead)
        {
            context.Fail(AbilityFailure.CasterDead);
            return false;
        }

        if (target is not null && !definition.AffectsDead && target.Health.IsDead)
        {
            context.Fail(AbilityFailure.TargetDead);
            return false;
        }

        if (target is not null && context.Range > 0)
        {
            var rangeCheck = CheckRange(_owner, target, context.Range, context.MinRange);
            if (rangeCheck.HasValue)
            {
                context.Fail(rangeCheck.Value);
                return false;
            }
        }

        var ccCheck = CheckCrowdControl(_owner, definition);
        if (ccCheck.HasValue)
        {
            context.Fail(ccCheck.Value);
            return false;
        }

        // === Mutations start here ===

        // Set GCD
        if (!definition.IgnoreGlobalCooldown)
            SetGlobalCooldown(tick);

        // Register active cast
        ActiveCast = context;
        RangeCheckDone = false;

        switch (context.CastState)
        {
            case CastState.Instant:
                if (CompleteCast(context, tick))
                {
                    OnCastCompleted?.Invoke(context);
                    NotifyCooldown(context);
                }
                else
                {
                    OnCastFailed?.Invoke(context,
                        context.FailureCode ?? AbilityFailure.Cancelled);
                }
                break;

            case CastState.Casting:
                context.CastStartTime = tick;
                OnCastConfirmed?.Invoke(context);
                break;

            case CastState.Channeling:
                context.CastStartTime = tick;
                var interval = definition.ChannelInterval > 0
                    ? definition.ChannelInterval
                    : 1000;
                NextChannelTick = tick + interval;
                OnCastConfirmed?.Invoke(context);
                break;
        }

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PHASE 3: UPDATE (region thread tick)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Tick the active cast. Called once per region tick from <see cref="UnitEntity.Update"/>.
    /// Signals lifecycle transitions through domain callbacks (<see cref="OnCastCompleted"/>,
    /// <see cref="OnCastFailed"/>, <see cref="OnCooldownApplied"/>).
    /// </summary>
    public void Update(long tick)
    {
        var context = ActiveCast;
        if (context is null)
            return;

        if (context.HasFailed)
        {
            var reason = context.FailureCode ?? AbilityFailure.Cancelled;
            ClearCast();
            OnCastFailed?.Invoke(context, reason);
            return;
        }

        switch (context.CastState)
        {
            case CastState.Casting:
                UpdateCasting(context, tick);
                break;
            case CastState.Channeling:
                UpdateChanneling(context, tick);
                break;
            // Instant casts are fully handled in ConfirmCast — never in ActiveCast.
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CANCEL & SETBACK
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Cancel the active cast with the given reason.
    /// <para>
    /// Called externally from actions (Phase 2) — the caller is responsible for
    /// dispatching the <see cref="AbilityCastFailed"/> event via the dispatcher.
    /// </para>
    /// </summary>
    public void CancelCast(AbilityFailure reason)
    {
        var context = ActiveCast;
        if (context is null)
            return;

        context.Fail(reason);

        // Cast-bar abilities clear the GCD when interrupted (matching V1).
        if (context.Definition.CastTime > 0)
            ClearGlobalCooldown();

        ClearCast();
    }

    /// <summary>
    /// Apply setback to the active cast-bar ability (from being hit while casting).
    /// Extends the remaining cast time. For Fragile ≥ 2 abilities, interrupts immediately.
    /// </summary>
    /// <param name="delayMs">Milliseconds of delay to add to the cast.</param>
    public void AddSetback(float delayMs)
    {
        var context = ActiveCast;
        if (context is null || context.CastState != CastState.Casting)
            return;

        if (context.Definition.Fragile >= 2)
        {
            CancelCast(AbilityFailure.Interrupted);
            return;
        }

        context.SetbackAccumulator += delayMs;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CAST COMPLETION
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Completes the active cast: consumes resources, executes effects, applies cooldown.
    /// Returns <c>true</c> if the cast completed successfully; <c>false</c> if it failed
    /// (e.g. caster died).
    /// </summary>
    internal bool CompleteCast(AbilityCastContext context, long tick)
    {
        var definition = context.Definition;

        // Final caster-alive check
        if (_owner.Health.IsDead)
        {
            context.Fail(AbilityFailure.CasterDead);
            ClearCast();
            return false;
        }

        // Consume AP
        if (context.ApCost > 0)
            _owner.ActionPoints = Math.Max(0, _owner.ActionPoints - (int)context.ApCost);

        // Consume career resource
        if (context.SpecialCost > 0)
            _owner.TryGet<CareerResourceComponent>()?.Resource.Consume((int)context.SpecialCost);

        // Apply post-cast modifiers
        if (!definition.IgnoreOwnModifiers)
            ApplyModifiers(context, ModifierStage.PostCast);

        // Execute commands
        EffectExecutor?.ExecuteCommands(context, _owner, context.Target, OnDamageDealt);

        // Apply cooldown
        ApplyCooldown(definition, context, tick);

        // Clear active cast
        ClearCast();

        return true;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  UPDATE HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private void UpdateCasting(AbilityCastContext context, long tick)
    {
        var elapsed = tick - context.CastStartTime;
        var totalCastTime = context.CastTime + context.SetbackAccumulator;

        // 60% range re-check (V1 feature — catches target moving out of range mid-cast)
        if (!RangeCheckDone && elapsed >= totalCastTime * 0.6f)
        {
            RangeCheckDone = true;
            if (context.Target is not null && context.Range > 0)
            {
                var rangeFailure = CheckRange(_owner, context.Target, context.Range, context.MinRange);
                if (rangeFailure.HasValue)
                {
                    CancelCastInternal(context, rangeFailure.Value);
                    return;
                }
            }
        }

        // Cast completion
        if (elapsed >= totalCastTime)
        {
            if (CompleteCast(context, tick))
            {
                OnCastCompleted?.Invoke(context);
                NotifyCooldown(context);
            }
            else
            {
                OnCastFailed?.Invoke(context,
                    context.FailureCode ?? AbilityFailure.Cancelled);
            }
        }
    }

    private void UpdateChanneling(AbilityCastContext context, long tick)
    {
        // Check target alive
        if (context.Target is not null && context.Target.Health.IsDead)
        {
            CancelCastInternal(context, AbilityFailure.TargetDead);
            return;
        }

        // Channel duration complete
        if (tick - context.CastStartTime >= context.CastTime)
        {
            ClearCast();
            OnCastCompleted?.Invoke(context);
            NotifyCooldown(context);
            return;
        }

        // Channel tick
        if (tick >= NextChannelTick)
        {
            // Consume AP per tick (V1 behavior)
            if (context.ApCost > 0 && _owner.ActionPoints < (int)context.ApCost)
            {
                CancelCastInternal(context, AbilityFailure.NotEnoughAp);
                return;
            }

            if (context.ApCost > 0)
                _owner.ActionPoints = Math.Max(0, _owner.ActionPoints - (int)context.ApCost);

            // Range re-check per tick
            if (context.Target is not null && context.Range > 0)
            {
                var rangeFailure = CheckRange(_owner, context.Target, context.Range, context.MinRange);
                if (rangeFailure.HasValue)
                {
                    CancelCastInternal(context, rangeFailure.Value);
                    return;
                }
            }

            // Apply channel effects
            EffectExecutor?.ExecuteCommands(context, _owner, context.Target, OnDamageDealt);

            var interval = context.Definition.ChannelInterval > 0
                ? context.Definition.ChannelInterval
                : 1000;
            NextChannelTick += interval;
        }
    }

    /// <summary>
    /// Internal cancel used during Phase 3 ticking — signals the failure through the callback.
    /// </summary>
    private void CancelCastInternal(AbilityCastContext context, AbilityFailure reason)
    {
        context.Fail(reason);

        if (context.Definition.CastTime > 0)
            ClearGlobalCooldown();

        ClearCast();
        OnCastFailed?.Invoke(context, reason);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  MODIFIER APPLICATION
    // ═══════════════════════════════════════════════════════════════════

    private void ApplyModifiers(AbilityCastContext context, ModifierStage stage)
    {
        var modifiers = context.Definition.Modifiers;
        for (var i = 0; i < modifiers.Count; i++)
        {
            if (modifiers[i].Stage == stage)
                ModifierApplicator.ApplyDefinition(context, modifiers[i], ConditionEvaluator);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  COOLDOWN
    // ═══════════════════════════════════════════════════════════════════

    private void ApplyCooldown(
        AbilityDefinition definition, AbilityCastContext context, long tick)
    {
        var cooldownMs = (int)context.Cooldown;
        if (cooldownMs <= 0)
            return;

        // Enforce cooldown cap (prevents stacking cooldown reduction to zero)
        if (definition.CooldownCap > 0 && cooldownMs < definition.CooldownCap)
            cooldownMs = definition.CooldownCap;

        var cdEntry = definition.CooldownEntry != 0 ? definition.CooldownEntry : definition.Entry;
        SetCooldown(cdEntry, tick, cooldownMs);
    }

    /// <summary>Signal a cooldown callback if the context has a non-zero cooldown.</summary>
    private void NotifyCooldown(AbilityCastContext context)
    {
        var cooldownMs = (int)context.Cooldown;
        if (cooldownMs <= 0)
            return;

        var cdEntry = context.Definition.CooldownEntry != 0
            ? context.Definition.CooldownEntry
            : context.Definition.Entry;

        OnCooldownApplied?.Invoke(cdEntry, cooldownMs);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  VALIDATION HELPERS
    // ═══════════════════════════════════════════════════════════════════

    private static AbilityFailure? CheckCrowdControl(UnitEntity caster, AbilityDefinition definition)
    {
        var cc = caster.Buffs.GetActiveCrowdControl();
        if (cc == CrowdControlFlags.None)
            return null;

        // Hard CC — blocks all casts
        if ((cc & CrowdControlFlags.Disabled) != 0)
            return AbilityFailure.Knockdown;

        // Silence blocks verbal (magic) abilities
        if ((cc & CrowdControlFlags.Silence) != 0 && definition.AbilityType == AbilityType.Verbal)
            return AbilityFailure.Silenced;

        // Disarm blocks melee and ranged abilities
        if ((cc & CrowdControlFlags.Disarm) != 0
            && definition.AbilityType is AbilityType.Melee or AbilityType.Ranged)
            return AbilityFailure.Disarmed;

        return null;
    }

    private static AbilityFailure? CheckRange(
        UnitEntity caster, UnitEntity target, float rangeFeet, float minRangeFeet)
    {
        long distSq = caster.Position.DistanceSquared2D(target.Position);

        if (rangeFeet > 0)
        {
            long rangeUnits = (long)(rangeFeet * RegionConstants.UnitsPerFoot);
            if (distSq > rangeUnits * rangeUnits)
                return AbilityFailure.OutOfRange;
        }

        if (minRangeFeet > 0)
        {
            long minRangeUnits = (long)(minRangeFeet * RegionConstants.UnitsPerFoot);
            if (distSq < minRangeUnits * minRangeUnits)
                return AbilityFailure.TooClose;
        }

        return null;
    }

    private static bool RequiresTarget(AbilityDefinition definition)
    {
        return definition.TargetType is CommandTargetType.Enemy
            or CommandTargetType.Ally
            or CommandTargetType.AllyOrSelf
            or CommandTargetType.CareerTarget
            or CommandTargetType.AllyOrCareerTarget;
    }
}
