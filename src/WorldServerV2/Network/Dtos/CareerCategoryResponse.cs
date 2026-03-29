using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_CAREER_CATEGORY</c> (0xEE) — header for a career/renown tree.
/// <para>
/// Sent once per tree. Used for all career trees (0–8, 16–19), mastery (7), and renown (9–15).
/// The wire format is: 16-byte header + pascal name + slot list + trailer.
/// </para>
/// </summary>
public class CareerCategoryResponse
{
    /// <summary>Tree identifier: 0–8, 9–15 = renown, 16–19 = tome/starting.</summary>
    public byte TreeId { get; set; }

    /// <summary>Sub-category (always 1).</summary>
    public byte SubCategory { get; set; } = 1;

    /// <summary>Padding.</summary>
    public byte Padding1 { get; set; }

    /// <summary>Total mastery/renown points spent.</summary>
    public byte PointsSpent { get; set; }

    /// <summary>Unspent points available.</summary>
    public byte PointsAvailable { get; set; }

    /// <summary>3 bytes padding.</summary>
    [FixedLength(3)]
    public byte[] Padding2 { get; set; } = new byte[3];

    /// <summary>Cost to respec (in copper).</summary>
    public uint RespecCost { get; set; }

    /// <summary>4-byte tree visual flags (varies per tree).</summary>
    [FixedLength(4)]
    public byte[] TreeFlags { get; set; } = new byte[4];

    /// <summary>Tree display name (pascal-encoded, no null terminator from serializer).</summary>
    [PascalString]
    public string TreeName { get; set; } = string.Empty;

    /// <summary>
    /// Slot index entries. The serializer auto-writes a 2-byte length prefix (slot count)
    /// followed by <c>N × (zero:u8, index:u8)</c>.
    /// </summary>
    [PacketLength(2)]
    public CareerCategorySlotEntry[] Slots { get; set; } = [];

    /// <summary>3-byte trailer.</summary>
    [FixedLength(3)]
    public byte[] Trailer { get; set; } = new byte[3];
}

/// <summary>
/// A slot index entry within a <see cref="CareerCategoryResponse"/>.
/// </summary>
public class CareerCategorySlotEntry
{
    /// <summary>Always 0.</summary>
    public byte Zero { get; set; }
    
    /// <summary>1-based slot index.</summary>
    public byte Index { get; set; }
}
