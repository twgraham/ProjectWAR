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
    /// <summary>Pre-built raw packet payload.</summary>
    [RawBytes]
    public required byte[] Data { get; set; }

    // ═══════════════════════════════════════════════════════════════════
    //  FACTORY METHODS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Standard ability damage packet.
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
        byte damageEvent = wasCritical ? (byte)9 : (byte)1; // ABILITY_CRITICAL or ABILITY_HIT
        byte flags = absorption > 0 ? (byte)0x2A : (byte)0x07;

        return Build(casterOid, targetOid, abilityEntry, subIndex, damageEvent, flags,
            -(int)(ushort)damage,
            mitigation > 0 ? (int)(ushort)mitigation : null,
            absorption > 0 ? (int)(ushort)absorption : null);
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
        byte damageEvent = defenseType switch
        {
            DefenseType.Block => 4,   // COMBATEVENT_BLOCK
            DefenseType.Parry => 5,   // COMBATEVENT_PARRY
            DefenseType.Evade => 6,   // COMBATEVENT_EVADE
            DefenseType.Disrupt => 7, // COMBATEVENT_DISRUPT
            _ => 0,
        };

        var data = new byte[10];
        WriteBigEndianU16(data, 0, casterOid);
        WriteBigEndianU16(data, 2, targetOid);
        WriteBigEndianU16(data, 4, abilityEntry);
        data[6] = 0;             // subIndex
        data[7] = damageEvent;
        data[8] = 0x05;          // flags = ability defense
        data[9] = 0;             // terminator

        return new CastPlayerEffectResponse { Data = data };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PACKET BUILDER
    // ═══════════════════════════════════════════════════════════════════

    private static CastPlayerEffectResponse Build(
        ushort casterOid, ushort targetOid, ushort abilityEntry,
        byte subIndex, byte damageEvent, byte flags,
        int damageZigZag, int? mitigationZigZag, int? absorptionZigZag)
    {
        // Header: 9 bytes  +  zigzag values  +  1 terminator
        // Worst-case zigzag per value: 5 bytes. Max 3 values = 15.
        // So max total = 9 + 15 + 1 = 25 bytes.
        Span<byte> buf = stackalloc byte[25];
        var pos = 0;

        WriteBigEndianU16(buf, 0, casterOid); pos += 2;
        WriteBigEndianU16(buf, 2, targetOid); pos += 2;
        WriteBigEndianU16(buf, 4, abilityEntry); pos += 2;
        buf[pos++] = subIndex;
        buf[pos++] = damageEvent;
        buf[pos++] = flags;

        pos += WriteZigZag(buf[pos..], damageZigZag);
        if (mitigationZigZag.HasValue)
            pos += WriteZigZag(buf[pos..], mitigationZigZag.Value);
        if (absorptionZigZag.HasValue)
            pos += WriteZigZag(buf[pos..], absorptionZigZag.Value);

        buf[pos++] = 0; // terminator

        return new CastPlayerEffectResponse { Data = buf[..pos].ToArray() };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ENCODING HELPERS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// WAR-protocol ZigZag encoding: variable-length signed integer.
    /// First byte carries sign (bit 0), 6 data bits (bits 1-6), continuation flag (bit 7).
    /// Subsequent bytes carry 7 data bits each with continuation flag.
    /// </summary>
    internal static int WriteZigZag(Span<byte> dest, int val)
    {
        byte sign = (byte)(val < 0 ? 1 : 0);
        if (sign == 1)
            val++;
        val = Math.Abs(val);

        var pos = 0;
        dest[pos++] = (byte)(((val << 1) & 0x7F) | (val > 0x3F ? 0x80 : 0x00) | sign);
        val >>= 6;

        while (val > 0)
        {
            dest[pos++] = (byte)((val & 0x7F) | (val > 0x7F ? 0x80 : 0x00));
            val >>= 7;
        }

        return pos;
    }

    private static void WriteBigEndianU16(Span<byte> dest, int offset, ushort value)
    {
        dest[offset] = (byte)(value >> 8);
        dest[offset + 1] = (byte)value;
    }
}
