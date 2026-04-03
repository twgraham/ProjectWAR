using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// The three variants of <c>F_PLAYER_STATE2</c> (0x62), determined by payload length.
/// </summary>
public enum PlayerStateType
{
    /// <summary>
    /// State-only update (≤ 9 bytes). Contains movement flags and a 3-bit heartbeat
    /// counter but no position data. Sent periodically when the player's position has
    /// not changed.
    /// </summary>
    Heartbeat,

    /// <summary>
    /// Standard movement packet (10–17 bytes). Contains full position, heading, zone ID,
    /// and movement state. No hostile target data.
    /// </summary>
    Standard,

    /// <summary>
    /// Combat movement packet (≥ 18 bytes). Same as <see cref="Standard"/> but with
    /// additional combat-target fields (line-of-sight, target data) and a shifted bit
    /// layout for coordinates.
    /// </summary>
    Combat
}

/// <summary>
/// Fields common to all <c>F_PLAYER_STATE2</c> packet variants. These are always present
/// regardless of the <see cref="PlayerStateType"/>.
/// </summary>
public readonly struct PlayerStateCommon
{
    /// <summary>16-bit entity status word (from entity+0x08).</summary>
    public required ushort Pid { get; init; }

    /// <summary>Alternate coordinate mode flag. Always 0 in practice.</summary>
    public required bool AltMode { get; init; }

    /// <summary>Horizontal movement speed, decoded from ranged encoding [−127, 325].</summary>
    public required int Speed { get; init; }

    /// <summary>Vertical velocity (fall/jump), decoded from ranged encoding [−2000, 500].</summary>
    public required int VerticalVelocity { get; init; }

    /// <summary>Whether the player has an active hostile target selected.</summary>
    public required bool HasCombatTarget { get; init; }

    /// <summary>Movement mode: 0 = idle, 1 = forward, 2 = walk, 3 = backwards.</summary>
    public required byte MovementMode { get; init; }

    /// <summary>Movement direction (0–7, cardinal/intercardinal from heading angle).</summary>
    public required byte Direction { get; init; }

    /// <summary>Movement flags bitmask: bit 0 = grounded, bit 1 = airborne, bit 2 = swimming.</summary>
    public required byte MovementFlags { get; init; }

    /// <summary>3-bit heartbeat counter (0–7), cycling with each periodic update.</summary>
    public required byte Heartbeat { get; init; }

    /// <summary>Whether this packet contains position data.</summary>
    public required bool HasPosition { get; init; }

    /// <summary>Not-moving flag (player is stationary).</summary>
    public required bool NotMoving { get; init; }

    /// <summary>Walking mode active (reduced speed).</summary>
    public required bool Walking { get; init; }

    /// <summary>Entity has an active buff or effect.</summary>
    public required bool HasActiveEffect { get; init; }

    /// <summary>Entity has a pending movement destination.</summary>
    public required bool HasMoveTarget { get; init; }
}

/// <summary>
/// Position and orientation fields present when <see cref="PlayerStateCommon.HasPosition"/>
/// is <c>true</c> and there is no click-to-move destination active. This is the normal
/// movement case for both standard and combat packets.
/// </summary>
public readonly struct PlayerStatePosition
{
    /// <summary>Common fields shared across all variants.</summary>
    public required PlayerStateCommon Common { get; init; }

    /// <summary>Facing direction as a 12-bit angle (radians encoded as fixed-point float).</summary>
    public required float Heading { get; init; }

    /// <summary>Ground surface type: 0 = solid, 1 = water/swimming.</summary>
    public required bool InWater { get; init; }

    /// <summary>Zone-local X coordinate (16-bit).</summary>
    public required ushort X { get; init; }

    /// <summary>Zone-local Y coordinate (16-bit).</summary>
    public required ushort Y { get; init; }

    /// <summary>Zone identifier (9-bit).</summary>
    public required ushort ZoneId { get; init; }

    /// <summary>Height / Z coordinate (16-bit unsigned).</summary>
    public required ushort Z { get; init; }
}

/// <summary>
/// Position fields when click-to-move is active (<see cref="PlayerStateCommon.HasPosition"/>
/// is <c>true</c> and HasMoveDestination is <c>true</c>). Replaces zone-local coordinates
/// with a click destination target.
/// </summary>
public readonly struct PlayerStateMoveDestination
{
    /// <summary>Common fields shared across all variants.</summary>
    public required PlayerStateCommon Common { get; init; }

    /// <summary>Facing direction as a 12-bit angle (radians encoded as fixed-point float).</summary>
    public required float Heading { get; init; }

    /// <summary>Ground surface type: 0 = solid, 1 = water/swimming.</summary>
    public required bool InWater { get; init; }

    /// <summary>Click-to-move destination X (16-bit signed).</summary>
    public required int TargetX { get; init; }

    /// <summary>Click-to-move destination Y (16-bit signed).</summary>
    public required int TargetY { get; init; }

    /// <summary>Click-to-move destination Z (16-bit signed).</summary>
    public required int TargetZ { get; init; }

    /// <summary>Target entity OID (9-bit real value as carried in the packet bitstream).</summary>
    public required ushort TargetOid { get; init; }
}
