namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Bitfield flags for the <c>F_OBJECT_STATE</c> (0x09) packet.
/// Controls which conditional fields are present in the wire-format tail.
/// </summary>
/// <remarks>
/// <para>
/// When <see cref="Moving"/> is set, the tail contains the destination block
/// (speed, destination position, destination zone). When clear, the tail contains
/// only the heading (u16 LE).
/// </para>
/// <para>
/// When <see cref="LookingAt"/> is set, a target OID (u16 LE) is appended after the
/// main tail (regardless of whether <see cref="Moving"/> is set).
/// </para>
/// <para>
/// V1 reference: <c>MovementInterface.WriteMovementState</c> builds this byte from
/// entity movement state, combat targets, and zone properties.
/// </para>
/// </remarks>
[Flags]
public enum ObjectStateFlags : byte
{
    /// <summary>No flags — entity is stationary, no target.</summary>
    None = 0,

    /// <summary>
    /// Bit 0 — entity has a destination (moving). Tail contains:
    /// Speed(u16 LE), DestUnk(u8), DestX(u16 LE), DestY(u16 LE), DestZ(u16 LE), DestZoneId(u8).
    /// When clear, tail contains only Heading(u16 LE).
    /// </summary>
    Moving = 1 << 0,

    /// <summary>
    /// Bit 1 — entity is looking at a target. Appends TargetOid(u16 LE) after
    /// the main tail (works with both stationary and moving states).
    /// </summary>
    LookingAt = 1 << 1,

    /// <summary>
    /// Bit 2 — zone ID exceeds 255 (extended zone support).
    /// V1 uses this as a hint for zone ID encoding.
    /// </summary>
    ExtendedZone = 1 << 2,

    /// <summary>
    /// Bit 4 — states update. Purpose not fully documented.
    /// </summary>
    States = 1 << 4,

    /// <summary>
    /// Bit 5 — no gravity / flying. V1 sets this for airborne creatures
    /// significantly above the terrain height.
    /// </summary>
    NoGravity = 1 << 5,

    /// <summary>
    /// Bits 3+5 (0x28) — recall state. V1: <c>MoveState == EMoveState.Recall</c>.
    /// Mutually exclusive with <see cref="LookingAt"/> in V1's logic.
    /// </summary>
    Recall = (1 << 3) | (1 << 5),
}
