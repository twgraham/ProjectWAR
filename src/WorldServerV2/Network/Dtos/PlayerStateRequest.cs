using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Inbound <c>F_PLAYER_STATE2</c> (0x62) packet. The client sends a variable-length
/// LSB-first bitstream containing movement state, position, heading, and optionally
/// combat target data. The raw payload is captured as-is so the handler can:
/// <list type="bullet">
///   <item>Determine the variant via <see cref="PlayerStateRequestExtensions.Type"/>.</item>
///   <item>Decode the common header fields via <see cref="PlayerStateRequestExtensions.DecodeCommon"/>.</item>
///   <item>Decode full position via <see cref="PlayerStateRequestExtensions.DecodePosition"/>.</item>
///   <item>Relay the packet verbatim to nearby clients.</item>
/// </list>
/// </summary>
public class PlayerStateRequest
{
    /// <summary>
    /// The complete raw payload of the packet. Consumed via <c>[RawBytes]</c> so the
    /// bitstream decoder can read fields at arbitrary bit positions without the serializer
    /// splitting the data at byte boundaries.
    /// </summary>
    [RawBytes]
    public required byte[] Data { get; set; }
}

/// <summary>
/// Extension methods for <see cref="PlayerStateRequest"/> that provide packet-type
/// classification and bitstream decoding into strongly-typed result models.
/// </summary>
public static class PlayerStateRequestExtensions
{
    /// <summary>Maximum float value for heading (radians). WriteFloat(12) encodes with this range.</summary>
    /// <remarks>Approximate 2π — matches the client's float constant for angle encoding.</remarks>
    private const float HeadingMaxRadians = 6.2831855f;

    extension(PlayerStateRequest req)
    {
        /// <summary>
        /// Determines the packet variant based on payload length.
        /// </summary>
        public PlayerStateType Type => req.Data.Length switch
        {
            <= 9 => PlayerStateType.Heartbeat,
            <= 17 => PlayerStateType.Standard,
            _ => PlayerStateType.Combat
        };

        /// <summary>
        /// Decodes the common header fields present in all packet variants (including
        /// heartbeats). These fields occupy the first 51 bits of the bitstream when
        /// combat-target data is absent, or 52 bits when it is present.
        /// </summary>
        public PlayerStateCommon DecodeCommon()
        {
            var reader = new BitReader(req.Data);
            return ReadCommon(ref reader);
        }

        /// <summary>
        /// Decodes a standard or combat movement packet into full position fields.
        /// Returns <c>null</c> if <see cref="PlayerStateCommon.HasPosition"/> is
        /// <c>false</c> (heartbeat packets — use <see cref="DecodeCommon"/> instead).
        /// <para>
        /// When the position data indicates a click-to-move destination, this method
        /// still returns a <see cref="PlayerStatePosition"/> but the coordinates
        /// represent the move destination, not the player's current zone-local position.
        /// Use <see cref="DecodeMoveDestination"/> if you need the typed destination model.
        /// </para>
        /// </summary>
        public PlayerStatePosition? DecodePosition()
        {
            var reader = new BitReader(req.Data);
            var common = ReadCommon(ref reader);

            if (!common.HasPosition)
                return null;

            // Field 12: Heading (WriteFloat, 12 bits — angle in radians)
            float heading = reader.ReadFloat(12, HeadingMaxRadians);

            // Field 13: HasMoveDestination (WriteBit)
            bool hasMoveDestination = reader.ReadBit();

            // Field 14: InWater/GroundType (WriteBit)
            bool inWater = reader.ReadBit();

            if (hasMoveDestination)
            {
                // Click-to-move path — no zone-local coords available.
                // Caller should use DecodeMoveDestination() for the full model.
                return null;
            }

            // Fields 15–16: Zone-local X, Y (WriteBits, 16 each)
            // The client writes X first then Y — confirmed by State2.cs Write()
            // and V1 MovementHandlers bit extraction order.
            ushort x = (ushort)reader.ReadBits(16);
            ushort y = (ushort)reader.ReadBits(16);

            // Field 17: Combat engagement (WriteBit) — conditional on combat & !alt
            if (common.HasCombatTarget && !common.AltMode)
                reader.ReadBit(); // skip — not needed for position decode

            // Field 18: Zone ID (WriteBits, 9)
            ushort zoneId = (ushort)reader.ReadBits(9);

            // Field 19: Z/Height (WriteBits, 16)
            ushort z = (ushort)reader.ReadBits(16);

            return new PlayerStatePosition
            {
                Common = common,
                Heading = heading,
                InWater = inWater,
                X = x,
                Y = y,
                ZoneId = zoneId,
                Z = z
            };
        }

        /// <summary>
        /// Decodes a movement packet that has a click-to-move destination active.
        /// Returns <c>null</c> if the packet is a heartbeat or does not contain a
        /// move destination.
        /// </summary>
        public PlayerStateMoveDestination? DecodeMoveDestination()
        {
            var reader = new BitReader(req.Data);
            var common = ReadCommon(ref reader);

            if (!common.HasPosition)
                return null;

            float heading = reader.ReadFloat(12, HeadingMaxRadians);
            bool hasMoveDestination = reader.ReadBit();
            bool inWater = reader.ReadBit();

            if (!hasMoveDestination)
                return null;

            // Fields 20–22: Target X, Y, Z (WriteSigned, 16 each)
            int targetX = reader.ReadSigned(16);
            int targetY = reader.ReadSigned(16);
            int targetZ = reader.ReadSigned(16);

            // Field 23: Target OID (WriteBits, 9)
            ushort targetOid = (ushort)reader.ReadBits(9);

            return new PlayerStateMoveDestination
            {
                Common = common,
                Heading = heading,
                InWater = inWater,
                TargetX = targetX,
                TargetY = targetY,
                TargetZ = targetZ,
                TargetOid = targetOid
            };
        }
    }

    /// <summary>
    /// Reads the common header fields from the bitstream, advancing the reader past
    /// all unconditional and conditionally-present header bits.
    /// </summary>
    private static PlayerStateCommon ReadCommon(ref BitReader reader)
    {
        // Field 1: PID / Status Word (WriteBits, 16)
        ushort pid = (ushort)reader.ReadBits(16);

        // Field 2: AltMode (WriteBit) — always 0 in practice
        bool altMode = reader.ReadBit();

        // Field 3: Speed (WriteRanged, -127 to 325 → 9 bits)
        int speed = reader.ReadRanged(-127, 325);

        // Field 4: Vertical Velocity (WriteRanged, -2000 to 500 → 12 bits)
        int verticalVelocity = reader.ReadRanged(-2000, 500);

        // Field 5: HasCombatTarget (WriteBit)
        bool hasCombatTarget = reader.ReadBit();

        // Field 6: Movement Mode (WriteBits, 2)
        byte movementMode = (byte)reader.ReadBits(2);

        // Field 7: Direction (WriteBits, 3)
        byte direction = (byte)reader.ReadBits(3);

        // Field 8: Movement Flags (WriteBits, 3)
        byte movementFlags = (byte)reader.ReadBits(3);

        // Field 9: Target Visibility (WriteBit) — only when hasCombatTarget && !altMode
        if (hasCombatTarget && !altMode)
            reader.ReadBit(); // consume but don't expose (combat relay data)

        // Field 10: Heartbeat counter (WriteBits, 3) — only when !altMode
        byte heartbeat = 0;
        if (!altMode)
            heartbeat = (byte)reader.ReadBits(3);

        // Field 11: HasPosition (WriteBit)
        bool hasPosition = reader.ReadBit();

        // Skip to the tail fields — we need to read past position data to reach them.
        // For DecodeCommon, we extract position-independent tail fields. However, the
        // tail fields are at variable bit offsets depending on HasPosition, combat, and
        // move-destination flags. Since the common model shouldn't need to parse
        // position internals, we read the tail flags only from heartbeat packets
        // where the bit positions are known.
        bool notMoving = false;
        bool walking = false;
        bool hasActiveEffect = false;
        bool hasMoveTarget = false;

        if (!hasPosition)
        {
            // Heartbeat: tail fields follow immediately.
            // Field 29: Animation/Stance (WriteBits, 3) — when !altMode
            if (!altMode)
                reader.ReadBits(3); // skip

            // Field 34: NotMoving (WriteBit) — always
            notMoving = reader.ReadBit();

            // Field 35: Walking (WriteBit) — when !altMode
            if (!altMode)
                walking = reader.ReadBit();

            // Field 36: HasActiveEffect (WriteBit) — always
            hasActiveEffect = reader.ReadBit();

            // Field 37: HasMoveTarget (WriteBit) — always
            hasMoveTarget = reader.ReadBit();
        }

        return new PlayerStateCommon
        {
            Pid = pid,
            AltMode = altMode,
            Speed = speed,
            VerticalVelocity = verticalVelocity,
            HasCombatTarget = hasCombatTarget,
            MovementMode = movementMode,
            Direction = direction,
            MovementFlags = movementFlags,
            Heartbeat = heartbeat,
            HasPosition = hasPosition,
            NotMoving = notMoving,
            Walking = walking,
            HasActiveEffect = hasActiveEffect,
            HasMoveTarget = hasMoveTarget
        };
    }
}