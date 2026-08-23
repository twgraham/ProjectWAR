using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// <c>F_USE_ABILITY</c> (0xDA) — Notifies the client of ability cast state changes.
/// <para>
/// The packet has a fixed 12-byte header followed by state-dependent fields controlled
/// by <c>[ConditionalOn(nameof(State), ...)]</c>. The source generator skips conditional
/// fields whose <see cref="State"/> does not match, producing the correct variable-length
/// wire format for each state:
/// </para>
/// <list type="bullet">
///   <item><c>0</c> — Cast cancelled / failed (20 bytes)</item>
///   <item><c>1</c> — Cast started (20 bytes)</item>
///   <item><c>2</c> — Cast completed (20 bytes)</item>
///   <item><c>6</c> — Projectile in flight (20 bytes)</item>
/// </list>
/// <para>
/// Property declaration order determines wire serialization order. Do not reorder properties.
/// </para>
/// </summary>
public class UseAbilityResponse
{
    // ── Fixed header (all states) — 12 bytes ─────────────────────────

    /// <summary>Reserved / unknown — always 0.</summary>
    public ushort Unknown { get; set; }

    /// <summary>Ability definition entry ID.</summary>
    public ushort AbilityEntry { get; set; }

    /// <summary>Runtime OID of the caster.</summary>
    public ushort CasterOid { get; set; }

    /// <summary>Effect ID from the ability definition.</summary>
    public ushort EffectId { get; set; }

    /// <summary>Runtime OID of the target (0 for self-cast / ground-targeted).</summary>
    public ushort TargetOid { get; set; }

    /// <summary>
    /// Cast state discriminator: 0 = cancelled, 1 = started, 2 = completed.
    /// Controls which conditional fields are serialized.
    /// </summary>
    public byte State { get; set; }

    // ── State 1, 2 & 6: Origin byte ────────────────────────────────────

    /// <summary>Ability origin (morale tier, career line, etc.) — states 1, 2, and 6.</summary>
    [ConditionalOn(nameof(State), 1, 2, 6)]
    public byte Origin { get; set; }

    // ── State 1: Cast started fields ─────────────────────────────────

    /// <summary>Cast time in milliseconds — state 1 (started) only.</summary>
    [ConditionalOn(nameof(State), 1)]
    public uint CastTime { get; set; }

    // ── State 2: Cast completed fields ───────────────────────────────

    /// <summary>Padding byte — state 2 only.</summary>
    [ConditionalOn(nameof(State), 2)]
    public byte CompletedPad { get; set; }

    /// <summary>Result code (0 = success) — state 2 only.</summary>
    [ConditionalOn(nameof(State), 2)]
    public byte Result { get; set; }

    /// <summary>Negative elapsed time for partial GCD reclaim — state 2 only.</summary>
    [ConditionalOn(nameof(State), 2)]
    public ushort ElapsedNeg { get; set; }

    // ── State 0: Cast cancelled fields ───────────────────────────────

    /// <summary>Flag byte (always 1) — state 0 (cancelled) only.</summary>
    [ConditionalOn(nameof(State), 0)]
    public byte CancelFlag { get; set; }

    /// <summary>Failure/cancel reason code — state 0 only.</summary>
    [ConditionalOn(nameof(State), 0)]
    public ushort FailCode { get; set; }

    /// <summary>Elapsed cast time before cancellation — state 0 only.</summary>
    [ConditionalOn(nameof(State), 0)]
    public ushort Elapsed { get; set; }

    // ── State 6: Projectile flight time fields ────────────────────────

    /// <summary>Padding byte 1 — state 6 (projectile) only.</summary>
    [ConditionalOn(nameof(State), 6)]
    public byte FlightTimePad1 { get; set; }

    /// <summary>Padding byte 2 — state 6 (projectile) only.</summary>
    [ConditionalOn(nameof(State), 6)]
    public byte FlightTimePad2 { get; set; }

    /// <summary>Projectile flight time in milliseconds — state 6 only.</summary>
    [ConditionalOn(nameof(State), 6)]
    public ushort FlightTimeMs { get; set; }

    // ── Common tail (all states) ─────────────────────────────────────

    /// <summary>Cast sequence number matching the client request.</summary>
    public byte CastSequence { get; set; }

    // ── Trailing padding (all states — V1 always writes 3 trailing bytes) ─

    /// <summary>Trailing padding — all states (u16).</summary>
    public ushort TrailingPad1 { get; set; }

    /// <summary>Trailing padding — all states (u8).</summary>
    public byte TrailingPad2 { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    //  FACTORY METHODS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Cast started — sent to nearby players when a cast begins (instant, cast-bar, or channel).
    /// </summary>
    public static UseAbilityResponse CastStarted(
        ushort abilityEntry,
        ushort casterOid,
        ushort effectId,
        ushort targetOid,
        byte origin,
        uint castTimeMs,
        byte castSequence)
    {
        return new UseAbilityResponse
        {
            AbilityEntry = abilityEntry,
            CasterOid = casterOid,
            EffectId = effectId,
            TargetOid = targetOid,
            State = 1,
            Origin = origin,
            CastTime = castTimeMs,
            CastSequence = castSequence,
        };
    }

    /// <summary>
    /// Cast completed — sent when the ability finishes executing.
    /// </summary>
    public static UseAbilityResponse CastCompleted(
        ushort abilityEntry,
        ushort casterOid,
        ushort effectId,
        ushort targetOid,
        byte origin,
        byte castSequence)
    {
        return new UseAbilityResponse
        {
            AbilityEntry = abilityEntry,
            CasterOid = casterOid,
            EffectId = effectId,
            TargetOid = targetOid,
            State = 2,
            Origin = origin,
            CastSequence = castSequence,
        };
    }

    /// <summary>
    /// Cast cancelled or failed — sent to the caster when a cast is rejected or interrupted.
    /// </summary>
    public static UseAbilityResponse CastCancelled(
        ushort abilityEntry,
        ushort casterOid,
        ushort effectId,
        ushort targetOid,
        byte failCode,
        byte castSequence)
    {
        return new UseAbilityResponse
        {
            AbilityEntry = abilityEntry,
            CasterOid = casterOid,
            EffectId = effectId,
            TargetOid = targetOid,
            State = 0,
            CancelFlag = 1,
            FailCode = failCode,
            CastSequence = castSequence,
        };
    }

    /// <summary>
    /// Projectile in flight — sent when a ranged ability fires a projectile.
    /// State 6 tells the client how long the projectile takes to reach the target.
    /// Damage is applied server-side after <paramref name="flightTimeMs"/> elapses.
    /// </summary>
    public static UseAbilityResponse ProjectileFlight(
        ushort abilityEntry,
        ushort casterOid,
        ushort effectId,
        ushort targetOid,
        ushort flightTimeMs,
        byte castSequence)
    {
        return new UseAbilityResponse
        {
            AbilityEntry = abilityEntry,
            CasterOid = casterOid,
            EffectId = effectId,
            TargetOid = targetOid,
            State = 6,
            Origin = 1,
            FlightTimeMs = flightTimeMs,
            CastSequence = castSequence,
        };
    }
}
