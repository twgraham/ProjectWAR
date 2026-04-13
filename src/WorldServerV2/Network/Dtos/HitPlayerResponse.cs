namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_HIT_PLAYER</c> (0x14).
/// Tells the client to update a target's health bar after damage/healing.
/// Broadcast to the target and all nearby players.
/// </summary>
/// <remarks>
/// Wire format (12 bytes):
/// <code>
/// UInt16  CasterOid      — OID of the attacker
/// UInt16  TargetOid      — OID of the entity whose health changed
/// UInt16  Unknown        — always 0
/// UInt16  Health         — current HP after the hit (clamped to ushort)
/// Byte    PctHealth      — health as percentage 0–100
/// Byte[3] Padding        — zeroes
/// </code>
/// </remarks>
public class HitPlayerResponse
{
    /// <summary>OID of the entity that caused the damage/heal.</summary>
    public ushort CasterOid { get; set; }

    /// <summary>OID of the entity whose health changed.</summary>
    public ushort TargetOid { get; set; }

    /// <summary>Unknown field — always 0.</summary>
    public ushort Unknown { get; set; }

    /// <summary>Current hit points after the hit (clamped to ushort range).</summary>
    public ushort Health { get; set; }

    /// <summary>Health as percentage 0–100.</summary>
    public byte PctHealth { get; set; }

    /// <summary>Padding byte 1.</summary>
    public byte Pad1 { get; set; }

    /// <summary>Padding byte 2.</summary>
    public byte Pad2 { get; set; }

    /// <summary>Padding byte 3.</summary>
    public byte Pad3 { get; set; }
}
