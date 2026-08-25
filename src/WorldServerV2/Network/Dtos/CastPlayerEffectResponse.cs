using Core.GameWorld.Combat;
using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// <c>F_CAST_PLAYER_EFFECT</c> (0xB3) — Displays a combat effect (damage, heal, defense)
/// on the client. Sent to the target, caster, and all nearby players.
/// <para>
/// The packet uses variable-length ZigZag encoding for damage/mitigation/absorption,
/// so the payload is pre-built as raw bytes via factory methods.
/// </para>
/// <para>
/// Wire layout:
/// <list type="number">
///   <item><c>u16</c> — Caster OID</item>
///   <item><c>u16</c> — Target OID</item>
///   <item><c>u16</c> — Ability entry (display ID)</item>
///   <item><c>u8</c>  — Sub-command index within the ability</item>
///   <item><c>u8</c>  — Damage event (CombatEvent enum: 0=hit, 1=ability_hit, 9=crit, etc.)</item>
///   <item><c>u8</c>  — Flags byte (5=defense, 7=ability, 0x13=auto-attack, 0x2A=absorbed)</item>
///   <item><c>zigzag</c> — Damage amount (negative for damage, positive for heals)</item>
///   <item><c>zigzag</c> — Mitigation (optional, only if &gt; 0)</item>
///   <item><c>zigzag</c> — Absorption (optional, only if &gt; 0)</item>
///   <item><c>u8</c>  — Terminator (0x00)</item>
/// </list>
/// </para>
/// </summary>
public class CastPlayerEffectResponse
{
    public ushort CasterId { get; set; }
    public ushort TargetId { get; set; }
    public ushort AbilityId { get; set; }
    public byte CommandIndex { get; set; }
    public CombatEvent Event { get; set; }
    public CombatFlags Flags { get; set; }
    
    [ZigZag]
    [ConditionalOn(nameof(Flags), CombatFlags.HasDamageData)]
    public int DamageAmount { get; set; }
    
    [ZigZag]
    [ConditionalOn(nameof(Flags), CombatFlags.HasDamageData)]
    public int MitigationAmount { get; set; }
    
    [ZigZag]
    [ConditionalOn(nameof(Flags), CombatFlags.HasAbsorptionData)]
    public int AbsorptionAmount { get; set; }

    /// <summary>Pre-built raw packet payload.</summary>
    [RawBytes]
    public byte[] Data { get; set; } = [];

    // ═══════════════════════════════════════════════════════════════════
    //  FACTORY METHODS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Standard ability damage packet (flags 0x07 = SelfTarget + HasDamage + ShowVisual).
    /// When absorption is present, uses flags 0x2A (HasDamage + SkipEffect + HasAbsorption)
    /// to match the client's conditional read of the absorption zigzag.
    /// </summary>
    public static CastPlayerEffectResponse Damage(
        ushort casterOid,
        ushort targetOid,
        ushort abilityEntry,
        byte subIndex,
        uint damage,
        uint mitigation,
        uint absorption,
        bool wasCritical)
    {
        var flags = CombatFlags.SelfTarget | CombatFlags.HasDamageData | CombatFlags.ShowVisual;
        if (absorption > 0)
            flags = CombatFlags.HasDamageData | CombatFlags.SkipEffectLogic | CombatFlags.HasAbsorptionData;

        return new CastPlayerEffectResponse
        {
            CasterId = casterOid,
            TargetId = targetOid,
            AbilityId = abilityEntry,
            CommandIndex = subIndex,
            Event = wasCritical ? CombatEvent.AbilityCritical : CombatEvent.Hit,
            Flags = flags,
            DamageAmount = -(int)damage,
            MitigationAmount = (int)mitigation,
            AbsorptionAmount = (int)absorption
        };
    }

    /// <summary>
    /// Auto-attack damage packet (flags 0x13 = SelfTarget + HasDamage + UseAlternateAbility).
    /// The client reads <c>UseAlternateAbility</c> to show the current weapon icon rather
    /// than an ability icon. CombatEvent is <c>Hit</c> or <c>Critical</c> (not AbilityHit).
    /// When absorption is present, uses flags 0x2A (HasDamage + SkipEffect + HasAbsorption).
    /// </summary>
    public static CastPlayerEffectResponse AutoAttackDamage(
        ushort casterOid,
        ushort targetOid,
        uint damage,
        uint mitigation,
        uint absorption,
        bool wasCritical)
    {
        var flags = absorption > 0
            ? CombatFlags.HasDamageData | CombatFlags.SkipEffectLogic | CombatFlags.HasAbsorptionData
            : CombatFlags.SelfTarget | CombatFlags.HasDamageData | CombatFlags.UseAlternateAbility;

        return new CastPlayerEffectResponse
        {
            CasterId = casterOid,
            TargetId = targetOid,
            AbilityId = 0,
            CommandIndex = 0,
            Event = wasCritical ? CombatEvent.Critical : CombatEvent.Hit,
            Flags = flags,
            DamageAmount = -(int)damage,
            MitigationAmount = (int)mitigation,
            AbsorptionAmount = (int)absorption
        };
    }

    /// <summary>
    /// Cast animation trigger — sent on ability execution to drive the client-side VFX.
    /// No damage is associated. The low byte of <paramref name="effectId"/> is passed as the
    /// sub-command index; the client uses it to look up and play the correct particle effect.
    /// <para>
    /// Wire layout: casterOid, targetOid, abilityEntry, (byte)effectId, combatEvent=0, flags=1, terminator=0.
    /// </para>
    /// </summary>
    /// <param name="targetOid">
    /// OID of the target. Pass the caster's OID for self-targeted or AoE abilities with no explicit target.
    /// </param>
    public static CastPlayerEffectResponse CastAnimation(
        ushort casterOid,
        ushort targetOid,
        ushort abilityEntry,
        ushort effectId)
    {
        return new CastPlayerEffectResponse
        {
            CasterId = casterOid,
            TargetId = targetOid,
            AbilityId = abilityEntry,
            CommandIndex = (byte)effectId,
            Event = CombatEvent.Hit,
            Flags = CombatFlags.SelfTarget
        };
    }

    /// <summary>
    /// Defense event (block, parry, evade, disrupt) — no damage numbers.
    /// </summary>
    public static CastPlayerEffectResponse Defense(
        ushort casterOid,
        ushort targetOid,
        ushort abilityEntry,
        DefenseType defenseType)
    {
        var damageEvent = defenseType switch
        {
            DefenseType.Block => CombatEvent.Block,
            DefenseType.Parry => CombatEvent.Parry,
            DefenseType.Evade => CombatEvent.Evade,
            DefenseType.Disrupt => CombatEvent.Disrupt,
            _ => CombatEvent.Hit
        };

        return new CastPlayerEffectResponse
        {
            CasterId = casterOid,
            TargetId = targetOid,
            AbilityId = abilityEntry,
            CommandIndex = 0,
            Event = damageEvent,
            Flags = CombatFlags.SelfTarget | CombatFlags.ShowVisual
        };
    }
}

public enum CombatEvent
{
    Hit = 0,
    AbilityHit = 1,
    Critical = 2,
    Block = 4,
    Parry = 5,
    Evade = 6,
    Disrupt = 7,
    Absorb = 8,
    AbilityCritical = 9,
    Immune = 10,
    FallDamage = 11
}

[Flags]
public enum CombatFlags : byte
{
    SelfTarget = 1 << 0,
    HasDamageData = 1 << 1,
    ShowVisual = 1 << 2,
    SkipEffectLogic = 1 << 3,
    UseAlternateAbility = 1 << 4,
    HasAbsorptionData = 1 << 5,
    HasExtendedData = 1 << 6
}
