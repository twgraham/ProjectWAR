namespace WorldServerV2.Network.Dtos;

/// <summary>
/// <c>F_SET_ABILITY_TIMER</c> (0x7E) — Cast-bar timer format.
/// <para>
/// Sent to the caster when a cast-bar ability begins or is set back by damage.
/// 12 bytes total. Sent only to the caster (not broadcast).
/// </para>
/// <para>
/// Wire layout (big-endian):
/// <c>u16 Flag=1 | u8 Unk=1 | u8 TimerType | u16 Pad=0 | u16 Duration | u16 AbilityEntry | u8 CastSequence | u8 Pad=0</c>
/// </para>
/// <para>
/// This DTO and <see cref="CooldownTimerResponse"/> share opcode 0x7E but have completely
/// disjoint wire formats — no common discriminator byte. They are modeled as separate
/// types rather than forcing a fragile union with <c>[ConditionalOn]</c>.
/// </para>
/// </summary>
public class CastBarTimerResponse
{
    /// <summary>Flag identifying this as a cast-bar packet (always 1).</summary>
    public ushort Flag { get; set; } = 1;

    /// <summary>Unknown — always 1 in V1.</summary>
    public byte Unk { get; set; } = 1;

    /// <summary>Timer type: 1 = initial cast bar, 3 = setback (cast pushed back by damage).</summary>
    public byte TimerType { get; set; }

    /// <summary>Padding (u16).</summary>
    public ushort Pad1 { get; set; }

    /// <summary>Cast duration or remaining time in milliseconds.</summary>
    public ushort Duration { get; set; }

    /// <summary>Ability definition entry ID.</summary>
    public ushort AbilityEntry { get; set; }

    /// <summary>Cast sequence number matching the client request.</summary>
    public byte CastSequence { get; set; }

    /// <summary>Trailing padding byte.</summary>
    public byte Pad2 { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    //  FACTORY METHODS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Initial cast-bar timer — sent when a cast-bar ability begins.</summary>
    public static CastBarTimerResponse CastBar(ushort abilityEntry, ushort castTimeMs, byte castSequence)
    {
        return new CastBarTimerResponse
        {
            TimerType = 1,
            Duration = castTimeMs,
            AbilityEntry = abilityEntry,
            CastSequence = castSequence,
        };
    }

    /// <summary>Setback — sent when a caster takes damage and the cast bar is pushed back.</summary>
    public static CastBarTimerResponse Setback(ushort abilityEntry, ushort remainingMs, byte castSequence)
    {
        return new CastBarTimerResponse
        {
            TimerType = 3,
            Duration = remainingMs,
            AbilityEntry = abilityEntry,
            CastSequence = castSequence,
        };
    }
}

/// <summary>
/// <c>F_SET_ABILITY_TIMER</c> (0x7E) — Cooldown timer format.
/// <para>
/// Sent to the caster after cast completion to show the ability cooldown in the action bar.
/// 12 bytes total. Sent only to the caster (not broadcast).
/// </para>
/// <para>
/// Wire layout (big-endian):
/// <c>u16 AbilityEntry | u16 Flags | u32 CooldownMs | u32 Pad=0</c>
/// </para>
/// <para>
/// See <see cref="CastBarTimerResponse"/> for the companion cast-bar format on the same opcode.
/// </para>
/// </summary>
public class CooldownTimerResponse
{
    /// <summary>Ability entry ID (0 for morale cooldown).</summary>
    public ushort AbilityEntry { get; set; }

    /// <summary>Flags: 0 = normal cooldown, 0x200 = morale cooldown.</summary>
    public ushort Flags { get; set; }

    /// <summary>Cooldown duration in milliseconds.</summary>
    public uint CooldownMs { get; set; }

    /// <summary>Trailing padding (u32).</summary>
    public uint Pad { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    //  FACTORY METHODS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Ability cooldown timer — sent after cast completion.</summary>
    public static CooldownTimerResponse Cooldown(ushort abilityEntry, uint cooldownMs)
    {
        return new CooldownTimerResponse
        {
            AbilityEntry = abilityEntry,
            CooldownMs = cooldownMs,
        };
    }

    /// <summary>Morale cooldown timer — sent after a morale ability is used.</summary>
    public static CooldownTimerResponse MoraleCooldown(uint cooldownMs)
    {
        return new CooldownTimerResponse
        {
            Flags = 0x200,
            CooldownMs = cooldownMs,
        };
    }
}
