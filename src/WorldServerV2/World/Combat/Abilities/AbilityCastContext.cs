using WorldServerV2.World.Entities;

namespace WorldServerV2.World.Combat.Abilities;

/// <summary>
/// Mutable per-cast state for an ability in progress. Created from an
/// <see cref="AbilityDefinition"/> at cast initiation, modified by modifiers,
/// and consumed during execution.
/// <para>
/// Replaces V1's approach of cloning the mutable <c>AbilityInfo</c> per-cast.
/// The <see cref="Definition"/> reference is immutable; all per-cast overrides
/// live here as flat fields.
/// </para>
/// </summary>
public sealed class AbilityCastContext
{
    // ═══════════════════════════════════════════════════════════════════
    //  DEFINITION (immutable reference)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>The immutable ability definition this cast is based on.</summary>
    public AbilityDefinition Definition { get; }

    // ═══════════════════════════════════════════════════════════════════
    //  PARTICIPANTS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>The entity casting the ability.</summary>
    public UnitEntity Caster { get; }

    /// <summary>The primary target entity (null for ground-targeted).</summary>
    public UnitEntity? Target { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    //  MODIFIABLE CAST PARAMETERS
    //  Initialized from Definition, then mutated by modifiers.
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Effective cast time in ms (may be modified by tactics/stats).</summary>
    public float CastTime { get; set; }

    /// <summary>Effective cooldown in ms.</summary>
    public float Cooldown { get; set; }

    /// <summary>Effective AP cost.</summary>
    public float ApCost { get; set; }

    /// <summary>Effective special resource cost.</summary>
    public float SpecialCost { get; set; }

    /// <summary>Effective range in feet.</summary>
    public float Range { get; set; }

    /// <summary>Effective minimum range in feet.</summary>
    public float MinRange { get; set; }

    /// <summary>Maximum targets for AoE.</summary>
    public int MaxTargets { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    //  DAMAGE / HEAL MODIFIERS (accumulated by modifiers, consumed by pipeline)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Multiplicative damage bonus accumulated from modifiers. Default 1.0.
    /// Applied as a multiplier in <see cref="DamagePipeline"/>.
    /// </summary>
    public float DamageBonus { get; set; } = 1f;

    /// <summary>
    /// Multiplicative damage reduction from modifiers. Default 1.0.
    /// </summary>
    public float DamageReduction { get; set; } = 1f;

    /// <summary>Flat crit-rate bonus from modifiers.</summary>
    public float CritBonus { get; set; }

    /// <summary>Crit damage multiplier bonus from modifiers.</summary>
    public float CritDamageBonus { get; set; }

    /// <summary>Armor/resist pen factor override from modifiers.</summary>
    public float ArmorPenBonus { get; set; }

    /// <summary>Defensibility modifier (positive = easier to defend against).</summary>
    public int Defensibility { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    //  FLAGS (set by modifiers or combat state)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Cannot be defended (block/parry/evade/disrupt).</summary>
    public bool IsUndefendable { get; set; }

    /// <summary>Can cast while moving (override from modifier).</summary>
    public bool CanCastWhileMoving { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    //  CAST STATE (managed by AbilityCastService, Step 5)
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Current cast state.</summary>
    public CastState CastState { get; set; }

    /// <summary>Tick timestamp when the cast started.</summary>
    public long CastStartTime { get; set; }

    /// <summary>Cast sequence ID from the client packet (for dedup/ordering).</summary>
    public byte CastSequence { get; set; }

    /// <summary>
    /// Accumulated setback from being hit while casting (0.0–1.0).
    /// When >= 1.0 the cast is interrupted.
    /// </summary>
    public float SetbackAccumulator { get; set; }

    /// <summary>
    /// Failure code if the cast was rejected or interrupted.
    /// Null = cast is still valid.
    /// </summary>
    public AbilityFailure? FailureCode { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    //  CONSTRUCTOR — snapshot definition defaults
    // ═══════════════════════════════════════════════════════════════════

    public AbilityCastContext(AbilityDefinition definition, UnitEntity caster, UnitEntity? target = null)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Caster = caster ?? throw new ArgumentNullException(nameof(caster));
        Target = target;

        // Snapshot mutable copies from the immutable definition.
        CastTime = definition.CastTime;
        Cooldown = definition.Cooldown;
        ApCost = definition.ApCost;
        SpecialCost = definition.SpecialCost;
        Range = definition.Range;
        MinRange = definition.MinRange;
        MaxTargets = definition.MaxTargets == 0 ? 9 : definition.MaxTargets;
        CanCastWhileMoving = definition.CanCastWhileMoving;

        // Determine initial cast state.
        if (definition.ChannelId > 0)
            CastState = CastState.Channeling;
        else if (definition.CastTime > 0)
            CastState = CastState.Casting;
        else
            CastState = CastState.Instant;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DERIVED STATE
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>True if the cast has been flagged as failed.</summary>
    public bool HasFailed => FailureCode.HasValue;

    /// <summary>True if this is an instant-cast ability.</summary>
    public bool IsInstant => CastState == CastState.Instant;

    /// <summary>True if the cast is currently in a cast-bar state.</summary>
    public bool IsCasting => CastState == CastState.Casting;

    /// <summary>True if the ability is channeling.</summary>
    public bool IsChanneling => CastState == CastState.Channeling;

    /// <summary>
    /// Mark the cast as failed with the given reason.
    /// </summary>
    public void Fail(AbilityFailure failure)
    {
        FailureCode = failure;
    }
}
