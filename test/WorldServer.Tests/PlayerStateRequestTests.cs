using System.IO;
using System.Numerics;
using Shouldly;
using WorldServerV2.Network.Dtos;

namespace WorldServer.Tests;

/// <summary>
/// Tests for <see cref="PlayerStateRequest"/> variant classification and bitstream
/// decode methods (<see cref="PlayerStateRequestExtensions.DecodeCommon"/>,
/// <see cref="PlayerStateRequestExtensions.DecodePosition"/>,
/// <see cref="PlayerStateRequestExtensions.DecodeMoveDestination"/>).
///
/// A <see cref="BitWriter"/> helper (mirroring the client LSB-first encoding) is used
/// to construct payloads with known field values so decode round-trips can be verified.
/// </summary>
public class PlayerStateRequestTests
{
    private const float HeadingMaxRadians = 6.2831855f;

    #region Type classification

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    public void Type_ShortPayload_ReturnsHeartbeat(int length)
    {
        var req = new PlayerStateRequest { Data = new byte[length] };
        req.Type.ShouldBe(PlayerStateType.Heartbeat);
    }

    [Theory]
    [InlineData(10)]
    [InlineData(17)]
    public void Type_MediumPayload_ReturnsStandard(int length)
    {
        var req = new PlayerStateRequest { Data = new byte[length] };
        req.Type.ShouldBe(PlayerStateType.Standard);
    }

    [Theory]
    [InlineData(18)]
    [InlineData(32)]
    public void Type_LongPayload_ReturnsCombat(int length)
    {
        var req = new PlayerStateRequest { Data = new byte[length] };
        req.Type.ShouldBe(PlayerStateType.Combat);
    }

    #endregion

    #region DecodeCommon

    [Fact]
    public void DecodeCommon_Heartbeat_ReturnsCorrectFields()
    {
        var data = BuildHeartbeat(
            pid: 100, speed: 0, vv: 0,
            movementMode: 0, direction: 2, movementFlags: 0,
            heartbeat: 3, notMoving: true, walking: false,
            hasActiveEffect: false, hasMoveTarget: true);
        var req = new PlayerStateRequest { Data = data };

        var common = req.DecodeCommon();

        common.Pid.ShouldBe((ushort)100);
        common.AltMode.ShouldBeFalse();
        common.Speed.ShouldBe(0);
        common.VerticalVelocity.ShouldBe(0);
        common.HasCombatTarget.ShouldBeFalse();
        common.MovementMode.ShouldBe((byte)0);
        common.Direction.ShouldBe((byte)2);
        common.MovementFlags.ShouldBe((byte)0);
        common.Heartbeat.ShouldBe((byte)3);
        common.HasPosition.ShouldBeFalse();
        common.NotMoving.ShouldBeTrue();
        common.Walking.ShouldBeFalse();
        common.HasActiveEffect.ShouldBeFalse();
        common.HasMoveTarget.ShouldBeTrue();
    }

    [Fact]
    public void DecodeCommon_WithHasCombatTarget_DecodesCorrectly()
    {
        var data = BuildHeartbeat(pid: 50, speed: 10, hasCombatTarget: true, heartbeat: 1);
        var req = new PlayerStateRequest { Data = data };

        var common = req.DecodeCommon();

        common.Pid.ShouldBe((ushort)50);
        common.HasCombatTarget.ShouldBeTrue();
        common.Speed.ShouldBe(10);
        common.Heartbeat.ShouldBe((byte)1);
    }

    [Fact]
    public void DecodeCommon_StandardPositionPacket_HasPositionTrue()
    {
        var data = BuildStandardPosition(pid: 200, speed: 100, heartbeat: 5, x: 1000, y: 2000, zoneId: 42, z: 500);
        var req = new PlayerStateRequest { Data = data };

        var common = req.DecodeCommon();

        common.Pid.ShouldBe((ushort)200);
        common.Speed.ShouldBe(100);
        common.Heartbeat.ShouldBe((byte)5);
        common.HasPosition.ShouldBeTrue();
    }

    #endregion

    #region DecodePosition

    [Fact]
    public void DecodePosition_Heartbeat_ReturnsNull()
    {
        var req = new PlayerStateRequest { Data = BuildHeartbeat() };
        req.DecodePosition().ShouldBeNull();
    }

    [Fact]
    public void DecodePosition_MoveDestinationPacket_ReturnsNull()
    {
        var req = new PlayerStateRequest { Data = BuildMoveDestination() };
        req.DecodePosition().ShouldBeNull();
    }

    [Fact]
    public void DecodePosition_StandardMovement_ReturnsPositionFields()
    {
        var data = BuildStandardPosition(
            pid: 200, speed: 100, direction: 3,
            movementMode: 1, movementFlags: 1, heartbeat: 5, heading: 0f,
            inWater: false, x: 1000, y: 2000, zoneId: 42, z: 500);
        var req = new PlayerStateRequest { Data = data };

        var pos = req.DecodePosition();

        pos.ShouldNotBeNull();
        pos.Value.Common.Pid.ShouldBe((ushort)200);
        pos.Value.Common.Speed.ShouldBe(100);
        pos.Value.Common.Direction.ShouldBe((byte)3);
        pos.Value.Heading.ShouldBe(0f);
        pos.Value.InWater.ShouldBeFalse();
        pos.Value.X.ShouldBe((ushort)1000);
        pos.Value.Y.ShouldBe((ushort)2000);
        pos.Value.ZoneId.ShouldBe((ushort)42);
        pos.Value.Z.ShouldBe((ushort)500);
    }

    #endregion

    #region DecodeMoveDestination

    [Fact]
    public void DecodeMoveDestination_Heartbeat_ReturnsNull()
    {
        var req = new PlayerStateRequest { Data = BuildHeartbeat() };
        req.DecodeMoveDestination().ShouldBeNull();
    }

    [Fact]
    public void DecodeMoveDestination_StandardPositionPacket_ReturnsNull()
    {
        var req = new PlayerStateRequest { Data = BuildStandardPosition(hasMoveDestination: false) };
        req.DecodeMoveDestination().ShouldBeNull();
    }

    [Fact]
    public void DecodeMoveDestination_MoveDestinationPacket_ReturnsMoveDestinationFields()
    {
        var data = BuildMoveDestination(
            pid: 300, speed: 50, heartbeat: 2,
            heading: 0f, inWater: false,
            targetX: 100, targetY: 200, targetZ: 50, targetOid: 7);
        var req = new PlayerStateRequest { Data = data };

        var dest = req.DecodeMoveDestination();

        dest.ShouldNotBeNull();
        dest.Value.Common.Pid.ShouldBe((ushort)300);
        dest.Value.Common.Speed.ShouldBe(50);
        dest.Value.InWater.ShouldBeFalse();
        dest.Value.TargetX.ShouldBe(100);
        dest.Value.TargetY.ShouldBe(200);
        dest.Value.TargetZ.ShouldBe(50);
        dest.Value.TargetOid.ShouldBe((ushort)7);
    }

    [Fact]
    public void DecodeMoveDestination_NegativeTargetCoordinates_PreservesSign()
    {
        var data = BuildMoveDestination(targetX: -300, targetY: -150, targetZ: -10);
        var req = new PlayerStateRequest { Data = data };

        var dest = req.DecodeMoveDestination();

        dest.ShouldNotBeNull();
        dest.Value.TargetX.ShouldBe(-300);
        dest.Value.TargetY.ShouldBe(-150);
        dest.Value.TargetZ.ShouldBe(-10);
    }

    #endregion

    #region Malformed / truncated payload

    [Fact]
    public void DecodeCommon_EmptyPayload_ThrowsInvalidDataException()
    {
        var req = new PlayerStateRequest { Data = [] };
        Should.Throw<InvalidDataException>(() => req.DecodeCommon());
    }

    [Fact]
    public void DecodeCommon_TruncatedPayload_ThrowsInvalidDataException()
    {
        // 1 byte is far too short for all common fields (minimum ~7 bytes needed)
        var req = new PlayerStateRequest { Data = [0xFF] };
        Should.Throw<InvalidDataException>(() => req.DecodeCommon());
    }

    [Fact]
    public void DecodePosition_TruncatedPayload_ThrowsInvalidDataException()
    {
        // 3 bytes starts decoding but is too short to complete the common fields
        var req = new PlayerStateRequest { Data = [0x01, 0x00, 0xFF] };
        Should.Throw<InvalidDataException>(() => req.DecodePosition());
    }

    #endregion

    #region Payload builders (mirror client LSB-first encoding)

    private static byte[] BuildHeartbeat(
        ushort pid = 100, bool altMode = false, int speed = 0, int vv = 0,
        bool hasCombatTarget = false, byte movementMode = 0, byte direction = 0,
        byte movementFlags = 0, byte heartbeat = 0, byte animation = 0,
        bool notMoving = false, bool walking = false,
        bool hasActiveEffect = false, bool hasMoveTarget = false)
    {
        var w = new BitWriter();
        w.WriteBits(pid, 16);
        w.WriteBit(altMode);
        w.WriteRanged(speed, -127, 325);
        w.WriteRanged(vv, -2000, 500);
        w.WriteBit(hasCombatTarget);
        w.WriteBits(movementMode, 2);
        w.WriteBits(direction, 3);
        w.WriteBits(movementFlags, 3);
        if (hasCombatTarget && !altMode)
            w.WriteBit(false);               // TargetVisibility
        if (!altMode)
            w.WriteBits(heartbeat, 3);
        w.WriteBit(false);                   // HasPosition = false
        // Heartbeat tail fields
        if (!altMode)
            w.WriteBits(animation, 3);
        w.WriteBit(notMoving);
        if (!altMode)
            w.WriteBit(walking);
        w.WriteBit(hasActiveEffect);
        w.WriteBit(hasMoveTarget);
        return w.ToArray();
    }

    private static byte[] BuildStandardPosition(
        ushort pid = 200, bool altMode = false, int speed = 100, int vv = 0,
        bool hasCombatTarget = false, byte movementMode = 1, byte direction = 2,
        byte movementFlags = 1, byte heartbeat = 5,
        float heading = 0f, bool hasMoveDestination = false, bool inWater = false,
        ushort x = 1000, ushort y = 2000, ushort zoneId = 42, ushort z = 500)
    {
        var w = new BitWriter();
        w.WriteBits(pid, 16);
        w.WriteBit(altMode);
        w.WriteRanged(speed, -127, 325);
        w.WriteRanged(vv, -2000, 500);
        w.WriteBit(hasCombatTarget);
        w.WriteBits(movementMode, 2);
        w.WriteBits(direction, 3);
        w.WriteBits(movementFlags, 3);
        if (hasCombatTarget && !altMode)
            w.WriteBit(false);               // TargetVisibility
        if (!altMode)
            w.WriteBits(heartbeat, 3);
        w.WriteBit(true);                    // HasPosition = true
        w.WriteFloat(heading, 12, HeadingMaxRadians);
        w.WriteBit(hasMoveDestination);
        w.WriteBit(inWater);
        if (!hasMoveDestination)
        {
            w.WriteBits(x, 16);
            w.WriteBits(y, 16);
            if (hasCombatTarget && !altMode)
                w.WriteBit(false);           // combat engagement bit
            w.WriteBits(zoneId, 9);
            w.WriteBits(z, 16);
        }
        return w.ToArray();
    }

    private static byte[] BuildMoveDestination(
        ushort pid = 300, int speed = 50, byte heartbeat = 2,
        float heading = 0f, bool inWater = false,
        int targetX = 100, int targetY = 200, int targetZ = 50, ushort targetOid = 7)
    {
        var w = new BitWriter();
        w.WriteBits(pid, 16);
        w.WriteBit(false);                   // altMode
        w.WriteRanged(speed, -127, 325);
        w.WriteRanged(0, -2000, 500);
        w.WriteBit(false);                   // hasCombatTarget
        w.WriteBits(0, 2);                   // movementMode
        w.WriteBits(0, 3);                   // direction
        w.WriteBits(0, 3);                   // movementFlags
        w.WriteBits(heartbeat, 3);
        w.WriteBit(true);                    // hasPosition
        w.WriteFloat(heading, 12, HeadingMaxRadians);
        w.WriteBit(true);                    // hasMoveDestination
        w.WriteBit(inWater);
        w.WriteSigned(targetX, 16);
        w.WriteSigned(targetY, 16);
        w.WriteSigned(targetZ, 16);
        w.WriteBits(targetOid, 9);
        return w.ToArray();
    }

    /// <summary>
    /// Writes bits in LSB-first order, mirroring the WAR client's bitstream encoding
    /// (WriteRanged / WriteSigned / WriteFloat family of functions).
    /// </summary>
    private sealed class BitWriter
    {
        private readonly List<bool> _bits = [];

        public void WriteBit(bool value) => _bits.Add(value);

        public void WriteBits(uint value, int count)
        {
            for (int i = 0; i < count; i++)
                WriteBit((value >> i & 1) != 0);
        }

        public void WriteRanged(int value, int min, int max)
        {
            int range = Math.Abs(max - min) + 1;
            int bitCount = BitsForRange(range);
            WriteBits((uint)(value - min), bitCount);
        }

        /// <summary>
        /// Writes a signed integer: magnitude bits first (totalBits−1), then sign bit.
        /// Matches the client's WriteSigned (sub_433364).
        /// </summary>
        public void WriteSigned(int value, int totalBits)
        {
            WriteBits((uint)Math.Abs(value), totalBits - 1);
            WriteBit(value < 0);
        }

        /// <summary>
        /// Writes a scaled fixed-point float via <see cref="WriteSigned"/>.
        /// Matches the client's WriteFloat (sub_4333FE).
        /// </summary>
        public void WriteFloat(float value, int totalBits, float maxValue)
        {
            float scale = (1 << (totalBits - 1)) - 1;
            int raw = (int)Math.Round(value * scale / maxValue);
            WriteSigned(raw, totalBits);
        }

        public byte[] ToArray()
        {
            int byteCount = (_bits.Count + 7) / 8;
            var result = new byte[byteCount];
            for (int i = 0; i < _bits.Count; i++)
            {
                if (_bits[i])
                    result[i / 8] |= (byte)(1 << (i % 8));
            }
            return result;
        }

        private static int BitsForRange(int range)
        {
            if (range <= 1) return 1;
            int bits = 32 - BitOperations.LeadingZeroCount((uint)(range - 1));
            if ((1 << bits) <= range)
                bits++;
            return bits;
        }
    }

    #endregion
}
