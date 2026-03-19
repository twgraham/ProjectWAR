using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

public class PlayerStateRequest
{
    public byte Unk1 { get; set; }
    public long State { get; set; }
    public long State2 { get; set; }
    
    [PacketLength(0)]
    public required byte[] ExtraData { get; set; }

    /// <summary>
    /// True when the packet is the long form (hostile target selected, packet size 18+), which
    /// packs higher-resolution coordinates and a different bit layout into
    /// <see cref="State"/>/<see cref="State2"/>. Derived from the presence of
    /// <see cref="ExtraData"/> bytes beyond the fixed 16-byte state block.
    /// </summary>
    public bool HasHostileTarget => ExtraData.Length > 0;

    // --- Computed fields (bit-extracted from State + State2) ---
    // State covers bits  0–63  of the 128-bit block.
    // State2 covers bits 64–127 (i.e. State2 bit N == global bit N+64).

    /// <summary>World X coordinate (16-bit).</summary>
    public ushort X => HasHostileTarget
        ? (ushort)(((State2 >> 56 & 0x3) << 14) | ((State >> 0 & 0xFF) << 6) | ((State >> 10 & 0x3F)))
        : (ushort)(((State2 >> 56 & 0x1) << 15) | ((State >> 0 & 0xFF) << 7) | ((State >> 9  & 0x7F)));

    /// <summary>World Y coordinate (16-bit).</summary>
    public ushort Y => HasHostileTarget
        ? (ushort)(((State2 >> 40 & 0x3) << 14) | ((State2 >> 48 & 0xFF) << 6) | ((State2 >> 58 & 0x3F)))
        : (ushort)(((State2 >> 40 & 0x1) << 15) | ((State2 >> 48 & 0xFF) << 7) | ((State2 >> 57 & 0x7F)));

    /// <summary>World Z coordinate (16-bit).</summary>
    public ushort Z => HasHostileTarget
        ? (ushort)(((State2 >> 16 & 0x7) << 12) | ((State2 >> 24 & 0xFF) << 4) | ((State2 >> 36 & 0x0F)))
        : (ushort)(((State2 >> 16 & 0x3) << 14) | ((State2 >> 24 & 0xFF) << 6) | ((State2 >> 34 & 0x3F)));

    /// <summary>Facing direction (12-bit heading).</summary>
    public ushort Direction => HasHostileTarget
        ? (ushort)(((State >> 16 & 0xFF) << 4) | ((State >> 28 & 0x0F)))
        : (ushort)(((State >> 16 & 0x7F) << 5) | ((State >> 27 & 0x1F)));

    /// <summary>Zone identifier (8-bit).</summary>
    public ushort ZoneId => HasHostileTarget
        ? (ushort)(((State2 >> 34 & 0x1) << 7) | ((State2 >> 32 & 0x3) << 5) | ((State2 >> 43 & 0x1F)))
        : (ushort)(((State2 >> 32 & 0x1) << 7) | ((State2 >> 41 & 0x7F)));

    /// <summary>True when the player is standing on the ground (not airborne).</summary>
    /// <remarks>Bit 8 in short form; bit 9 in long form.</remarks>
    public bool Grounded => HasHostileTarget
        ? (State >> 9 & 0x1) == 1
        : (State >> 8 & 0x1) == 1;

    /// <summary>5-bit fall state. 31 indicates a stable (non-falling) state.</summary>
    public byte FallState => (byte)(State >> 40 & 0x1F);

    /// <summary>True when the player is walking (reduced speed).</summary>
    public bool Walking => (State >> 48 & 0x1) == 1;

    /// <summary>True when the player is in motion.</summary>
    public bool Moving => (State >> 49 & 0x1) == 1;

    /// <summary>True when the player is stationary.</summary>
    public bool NotMoving => (State >> 63 & 0x1) == 1;

    /// <summary>
    /// Ground surface type the player is standing on.
    /// Bit 82 of 128-bit block (= State2 bit 18) in short form;
    /// bit 73 of 128-bit block (= State2 bit 9) in long form.
    /// </summary>
    public byte GroundType => HasHostileTarget
        ? (byte)(State2 >> 9  & 0x1F)  // global bit 73
        : (byte)(State2 >> 18 & 0x1F); // global bit 82
}