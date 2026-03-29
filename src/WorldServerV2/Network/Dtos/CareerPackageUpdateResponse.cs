using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_CAREER_PACKAGE_UPDATE</c> (0xF1) — mastery/renown point summary per tree.
/// <para>
/// Sent once per tree (3 for mastery, 7 for renown). Reports how many
/// points are spent in each tree and the respec cost.
/// </para>
/// </summary>
public class CareerPackageUpdateResponse
{
    /// <summary>Tree identifier (7 = mastery, 9–15 = renown).</summary>
    public byte TreeId { get; set; }

    /// <summary>Constant (always 1).</summary>
    public byte Const1 { get; set; } = 1;

    /// <summary>Constant (always 1).</summary>
    public byte Const2 { get; set; } = 1;

    /// <summary>Constant (always 1).</summary>
    public byte Const3 { get; set; } = 1;

    /// <summary>Unspent points available.</summary>
    public byte PointsAvailable { get; set; }

    /// <summary>2 bytes padding.</summary>
    [FixedLength(2)]
    public byte[] Padding1 { get; set; } = new byte[2];

    /// <summary>Tree index (1-based for mastery, from V1).</summary>
    public byte TreeIndex { get; set; }

    /// <summary>Points spent in this tree.</summary>
    public byte PointsSpent { get; set; }

    /// <summary>3 bytes padding.</summary>
    [FixedLength(3)]
    public byte[] Padding2 { get; set; } = new byte[3];

    /// <summary>Respec cost in copper.</summary>
    public uint RespecCost { get; set; }

    /// <summary>4-byte trailing zero.</summary>
    public uint Trailing { get; set; }
}
