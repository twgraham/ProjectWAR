using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// <c>F_UPDATE_STATE</c> (0xE4) — General-purpose state update broadcast for an entity.
/// <para>
/// Used for combat enter/leave, stunned, RvR flag, renown title, etc.
/// The V1 wire format is: OID (u16), StateOpcode (u8), val1 (u8), val2 (u8),
/// val3 (u8), padding (5 bytes) = 11 bytes total payload.
/// </para>
/// </summary>
public class UpdateStateResponse
{
    /// <summary>OID of the entity whose state changed.</summary>
    public ushort ObjectId { get; set; }

    /// <summary>
    /// State opcode discriminator.
    /// <list type="bullet">
    ///   <item>0x1A — Combat (val1: 1=enter, 0=leave)</item>
    ///   <item>0x0C — Stunned (val1: 1=stunned, 0=clear)</item>
    ///   <item>0x1E — RvRFlag</item>
    /// </list>
    /// </summary>
    public byte StateOpcode { get; set; }

    /// <summary>Primary value (state-dependent).</summary>
    public byte Value1 { get; set; }

    /// <summary>Secondary value (state-dependent, usually 0).</summary>
    public byte Value2 { get; set; }

    /// <summary>Tertiary value (guild heraldry in V1, usually 0).</summary>
    public byte Value3 { get; set; }

    /// <summary>Trailing padding to match V1 wire size (5 bytes).</summary>
    [FixedLength(5)]
    public byte[] Padding { get; set; } = new byte[5];

    // ═══════════════════════════════════════════════════════════════════
    //  FACTORY METHODS
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Combat state change — enters or leaves combat.
    /// </summary>
    public static UpdateStateResponse Combat(ushort objectId, bool enterCombat)
        => new()
        {
            ObjectId = objectId,
            StateOpcode = 0x1A,
            Value1 = enterCombat ? (byte)1 : (byte)0,
        };
}
