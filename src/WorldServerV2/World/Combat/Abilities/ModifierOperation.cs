namespace WorldServerV2.World.Combat.Abilities;

/// <summary>
/// Operations that ability modifiers can perform on an <see cref="AbilityCastContext"/>.
/// ~70% of V1's string-keyed modifier commands map directly to one of these.
/// <para>
/// Each value maps to a pure function <c>(AbilityCastContext, float value) → void</c>
/// in <see cref="ModifierApplicator"/>. Complex modifiers that don't fit a simple
/// operation use <see cref="Custom"/> and delegate to a registered
/// <c>IAbilityModifier</c>.
/// </para>
/// </summary>
public enum ModifierOperation : byte
{
    // ── Cast time ────────────────────────────────────────────────────
    /// <summary><c>context.CastTime *= value</c></summary>
    MultiplyCastTime = 0,

    /// <summary><c>context.CastTime += value</c></summary>
    AddCastTime = 1,

    /// <summary><c>context.CastTime = 0</c> (instant cast override)</summary>
    SetInstant = 2,

    /// <summary><c>context.CanCastWhileMoving = true</c></summary>
    SetMoveCast = 3,

    // ── Cooldown ─────────────────────────────────────────────────────
    /// <summary><c>context.Cooldown *= value</c></summary>
    MultiplyCooldown = 10,

    /// <summary><c>context.Cooldown += value</c> (value in ms)</summary>
    AddCooldown = 11,

    /// <summary><c>context.Cooldown = value</c></summary>
    SetCooldown = 12,

    // ── Action points ────────────────────────────────────────────────
    /// <summary><c>context.ApCost += value</c></summary>
    AddApCost = 20,

    /// <summary><c>context.ApCost = value</c></summary>
    SetApCost = 21,

    /// <summary><c>context.ApCost *= value</c></summary>
    MultiplyApCost = 22,

    // ── Special resource ─────────────────────────────────────────────
    /// <summary><c>context.SpecialCost += value</c></summary>
    AddSpecialCost = 25,

    // ── Range ────────────────────────────────────────────────────────
    /// <summary><c>context.Range *= value</c></summary>
    MultiplyRange = 30,

    /// <summary><c>context.Range += value</c></summary>
    AddRange = 31,

    // ── Damage / Healing ─────────────────────────────────────────────
    /// <summary><c>context.DamageBonus += value</c> (additive to multiplier)</summary>
    AddDamageBonus = 40,

    /// <summary><c>context.DamageBonus *= value</c></summary>
    MultiplyDamageBonus = 41,

    /// <summary><c>context.DamageReduction *= value</c></summary>
    MultiplyDamageReduction = 42,

    // ── Critical hit ─────────────────────────────────────────────────
    /// <summary><c>context.CritBonus += value</c> (flat % add)</summary>
    AddCritRate = 50,

    /// <summary><c>context.CritDamageBonus += value</c></summary>
    AddCritDamage = 51,

    // ── Defense ──────────────────────────────────────────────────────
    /// <summary><c>context.IsUndefendable = true</c></summary>
    SetUndefendable = 60,

    /// <summary><c>context.Defensibility += (int)value</c></summary>
    AddDefensibility = 61,

    // ── Armor penetration ────────────────────────────────────────────
    /// <summary><c>context.ArmorPenBonus += value</c></summary>
    AddArmorPenFactor = 70,

    // ── AoE / targeting ──────────────────────────────────────────────
    /// <summary><c>context.MaxTargets += (int)value</c></summary>
    AddMaxTargets = 80,

    // ── Custom ───────────────────────────────────────────────────────
    /// <summary>
    /// Complex modifier requiring an <c>IAbilityModifier</c> implementation.
    /// The applicator delegates to the registered handler. Used for ~30%
    /// of modifiers that manipulate commands, career-specific state, etc.
    /// </summary>
    Custom = 255,
}

/// <summary>
/// Condition checks evaluated before a modifier is applied. If the condition
/// fails, the modifier is skipped.
/// </summary>
public enum ModifierCondition : byte
{
    // ── Target state ─────────────────────────────────────────────────
    /// <summary>Target is behind the caster.</summary>
    IsBehind = 0,

    /// <summary>Target is flanking the caster.</summary>
    IsFlanking = 1,

    /// <summary>Target is currently casting an ability.</summary>
    TargetIsCasting = 2,

    /// <summary>Target HP is below ConditionValue %.</summary>
    TargetHpBelow = 3,

    /// <summary>Target is the principal/primary target (not AoE splash).</summary>
    IsPrincipalTarget = 4,

    /// <summary>Target is a player (not NPC).</summary>
    TargetIsPlayer = 5,

    /// <summary>Target is an organic unit (not siege/object).</summary>
    TargetIsOrganic = 6,

    // ── Caster state ─────────────────────────────────────────────────
    /// <summary>Caster is CC'd.</summary>
    IsCrowdControlled = 10,

    /// <summary>Caster's movement is impeded (root/snare).</summary>
    IsImpeded = 11,

    /// <summary>Caster can move (not rooted).</summary>
    CanMove = 12,

    /// <summary>Caster is out of combat.</summary>
    OutOfCombat = 13,

    /// <summary>Caster has backstab-eligible positioning + crit buff.</summary>
    HasCriticalBackstab = 14,

    /// <summary>Caster successfully defended in previous exchange.</summary>
    HasDefended = 15,

    /// <summary>Caster is grounded (cannot jump/fly).</summary>
    IsGrounded = 16,

    // ── Resource ─────────────────────────────────────────────────────
    /// <summary>Caster has at least ConditionValue career resource.</summary>
    HasResource = 20,

    // ── Buff checks ──────────────────────────────────────────────────
    /// <summary>Caster has buff with entry = ConditionValue.</summary>
    HasBuff = 30,

    /// <summary>Caster does NOT have buff with entry = ConditionValue.</summary>
    MissingBuff = 31,

    /// <summary>Target has buff with entry = ConditionValue.</summary>
    TargetHasBuff = 32,

    /// <summary>Caster has an active career buff.</summary>
    HasCareerBuff = 33,

    /// <summary>Caster has a buff of the specified BuffType.</summary>
    HasBuffOfType = 34,

    // ── Range / proximity ────────────────────────────────────────────
    /// <summary>Target is within ConditionValue feet.</summary>
    TargetWithinRange = 40,

    /// <summary>A hostile is within ConditionValue feet.</summary>
    HostileWithinRange = 41,

    // ── Ability type ─────────────────────────────────────────────────
    /// <summary>The ability is offensive and deals damage.</summary>
    IsOffensiveDamaging = 50,

    /// <summary>The ability is offensive (may not damage).</summary>
    IsOffensive = 51,
}

/// <summary>
/// Static applicator that maps <see cref="ModifierOperation"/> values to pure
/// functions on <see cref="AbilityCastContext"/>. This is the ~70% "simple"
/// modifier path — no string dispatch, no virtual calls.
/// </summary>
public static class ModifierApplicator
{
    /// <summary>
    /// Apply a single modifier to the cast context.
    /// Returns <c>false</c> if the operation is <see cref="ModifierOperation.Custom"/>
    /// (caller must delegate to a registered <c>IAbilityModifier</c>).
    /// </summary>
    public static bool Apply(AbilityCastContext context, ModifierOperation operation, float value)
    {
        switch (operation)
        {
            // ── Cast time ────────────────────────────────────────
            case ModifierOperation.MultiplyCastTime:
                context.CastTime *= value;
                return true;
            case ModifierOperation.AddCastTime:
                context.CastTime += value;
                return true;
            case ModifierOperation.SetInstant:
                context.CastTime = 0;
                context.CastState = CastState.Instant;
                return true;
            case ModifierOperation.SetMoveCast:
                context.CanCastWhileMoving = true;
                return true;

            // ── Cooldown ─────────────────────────────────────────
            case ModifierOperation.MultiplyCooldown:
                context.Cooldown *= value;
                return true;
            case ModifierOperation.AddCooldown:
                context.Cooldown += value;
                return true;
            case ModifierOperation.SetCooldown:
                context.Cooldown = value;
                return true;

            // ── AP cost ──────────────────────────────────────────
            case ModifierOperation.AddApCost:
                context.ApCost += value;
                return true;
            case ModifierOperation.SetApCost:
                context.ApCost = value;
                return true;
            case ModifierOperation.MultiplyApCost:
                context.ApCost *= value;
                return true;

            // ── Special cost ─────────────────────────────────────
            case ModifierOperation.AddSpecialCost:
                context.SpecialCost += value;
                return true;

            // ── Range ────────────────────────────────────────────
            case ModifierOperation.MultiplyRange:
                context.Range *= value;
                return true;
            case ModifierOperation.AddRange:
                context.Range += value;
                return true;

            // ── Damage ───────────────────────────────────────────
            case ModifierOperation.AddDamageBonus:
                context.DamageBonus += value;
                return true;
            case ModifierOperation.MultiplyDamageBonus:
                context.DamageBonus *= value;
                return true;
            case ModifierOperation.MultiplyDamageReduction:
                context.DamageReduction *= value;
                return true;

            // ── Crit ─────────────────────────────────────────────
            case ModifierOperation.AddCritRate:
                context.CritBonus += value;
                return true;
            case ModifierOperation.AddCritDamage:
                context.CritDamageBonus += value;
                return true;

            // ── Defense ──────────────────────────────────────────
            case ModifierOperation.SetUndefendable:
                context.IsUndefendable = true;
                return true;
            case ModifierOperation.AddDefensibility:
                context.Defensibility += (int)value;
                return true;

            // ── Armor pen ────────────────────────────────────────
            case ModifierOperation.AddArmorPenFactor:
                context.ArmorPenBonus += value;
                return true;

            // ── AoE ──────────────────────────────────────────────
            case ModifierOperation.AddMaxTargets:
                context.MaxTargets += (int)value;
                return true;

            // ── Custom ───────────────────────────────────────────
            case ModifierOperation.Custom:
                return false; // Caller must delegate to IAbilityModifier.

            default:
                return false;
        }
    }

    /// <summary>
    /// Apply a full modifier definition to the context. Checks conditions
    /// via a delegate, then applies the operation.
    /// </summary>
    /// <param name="context">The cast context to modify.</param>
    /// <param name="modifier">The modifier definition.</param>
    /// <param name="conditionEvaluator">
    /// Optional delegate to evaluate <see cref="ModifierCondition"/>. If null,
    /// conditional modifiers are skipped. Returns <c>true</c> if condition is met.
    /// </param>
    /// <returns>
    /// <c>true</c> if the modifier was applied (or skipped via condition);
    /// <c>false</c> if this is a <see cref="ModifierOperation.Custom"/> that needs
    /// external handling.
    /// </returns>
    public static bool ApplyDefinition(
        AbilityCastContext context,
        AbilityModifierDefinition modifier,
        Func<ModifierCondition, int, AbilityCastContext, bool>? conditionEvaluator = null)
    {
        // Check pre-condition.
        if (modifier.Condition.HasValue)
        {
            if (conditionEvaluator == null)
                return true; // No evaluator — skip conditional modifiers gracefully.

            if (!conditionEvaluator(modifier.Condition.Value, modifier.ConditionValue, context))
                return true; // Condition not met — skip (not a failure).
        }

        return Apply(context, modifier.Operation, modifier.Value);
    }
}
