using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_CAREER_PACKAGE_INFO</c> (0xF3) — mastery tree point count.
/// <para>
/// Sent once per mastery tree (3 packets). Tells the client how many points
/// are invested in a specific mastery tree.
/// </para>
/// </summary>
public class MasteryTreePointsResponse
{
    /// <summary>Career category (always 7 for mastery).</summary>
    public byte TreeId { get; set; } = 7;

    /// <summary>Sub-category (always 1).</summary>
    public byte SubCategory { get; set; } = 1;

    /// <summary>Padding.</summary>
    public byte Padding1 { get; set; }

    /// <summary>Tree position (1, 2, or 3).</summary>
    public byte Position { get; set; }

    /// <summary>4 bytes padding.</summary>
    [FixedLength(4)]
    public byte[] Padding2 { get; set; } = new byte[4];

    /// <summary>Points spent in this tree.</summary>
    public byte PointsSpent { get; set; }

    /// <summary>Flag (always 2).</summary>
    public byte Flag1 { get; set; } = 2;

    /// <summary>14 bytes padding.</summary>
    [FixedLength(14)]
    public byte[] Padding3 { get; set; } = new byte[14];

    /// <summary>Constant (always 1).</summary>
    public byte Const1 { get; set; } = 1;

    /// <summary>Constant (always 1).</summary>
    public byte Const2 { get; set; } = 1;

    /// <summary>2 bytes padding.</summary>
    [FixedLength(2)]
    public byte[] Padding4 { get; set; } = new byte[2];

    /// <summary>Visual flag byte 1 (always 2).</summary>
    public byte Visual1 { get; set; } = 2;

    /// <summary>Visual flag byte 2 (0x0D + tree index for trees 0/1/2).</summary>
    public byte Visual2 { get; set; }

    /// <summary>Visual flag byte 3 (always 6).</summary>
    public byte Visual3 { get; set; } = 6;

    /// <summary>5 bytes padding.</summary>
    [FixedLength(5)]
    public byte[] Padding5 { get; set; } = new byte[5];

    /// <summary>Tree visual flag (0x0F for trees 0/1, 0xFC for tree 2).</summary>
    public byte TreeVisualFlag { get; set; }

    /// <summary>5 bytes trailing padding.</summary>
    [FixedLength(5)]
    public byte[] Padding6 { get; set; } = new byte[5];
}
