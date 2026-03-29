using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_CAREER_PACKAGE_INFO</c> (0xF3) — individual mastery skill entry.
/// <para>
/// Sent once per mastery skill slot (3 trees × 7 slots = 21 packets).
/// Tells the client about a specific mastery ability and whether it's been purchased.
/// </para>
/// </summary>
public class MasterySkillResponse
{
    /// <summary>Career category (always 7 for mastery).</summary>
    public byte TreeId { get; set; } = 7;

    /// <summary>Sub-category (always 1).</summary>
    public byte SubCategory { get; set; } = 1;

    /// <summary>Padding.</summary>
    public byte Padding1 { get; set; }

    /// <summary>Sequential position index (starts at 4 for first skill).</summary>
    public byte Position { get; set; }

    /// <summary>4 bytes padding.</summary>
    [FixedLength(4)]
    public byte[] Padding2 { get; set; } = new byte[4];

    /// <summary>1 if this skill is purchased/active, 0 otherwise.</summary>
    public byte IsActive { get; set; }

    /// <summary>Flag (always 2).</summary>
    public byte Flag1 { get; set; } = 2;

    /// <summary>14 bytes padding.</summary>
    [FixedLength(14)]
    public byte[] Padding3 { get; set; } = new byte[14];

    /// <summary>Constant (always 1).</summary>
    public byte Const1 { get; set; } = 1;

    /// <summary>Constant (always 1).</summary>
    public byte Const2 { get; set; } = 1;

    /// <summary>2 bytes padding (0x00, 0x00).</summary>
    [FixedLength(2)]
    public byte[] Padding4 { get; set; } = new byte[2];

    /// <summary>Constant (0x14).</summary>
    public byte Const3 { get; set; } = 0x14;

    /// <summary>Constant (0x32).</summary>
    public byte Const4 { get; set; } = 0x32;

    /// <summary>Constant (2).</summary>
    public byte Const5 { get; set; } = 2;

    /// <summary>2 bytes padding.</summary>
    [FixedLength(2)]
    public byte[] Padding5 { get; set; } = new byte[2];

    /// <summary>The ability entry ID for this mastery skill.</summary>
    public ushort AbilityEntry { get; set; }

    /// <summary>18 bytes padding.</summary>
    [FixedLength(18)]
    public byte[] Padding6 { get; set; } = new byte[18];

    /// <summary>Ability name (pascal-encoded).</summary>
    [PascalString]
    public string AbilityName { get; set; } = string.Empty;

    /// <summary>Constant (always 1).</summary>
    public byte Const6 { get; set; } = 1;

    /// <summary>Padding.</summary>
    public byte Padding7 { get; set; }

    /// <summary>Tree number (1, 2, or 3).</summary>
    public byte TreeNumber { get; set; }

    /// <summary>Mastery point cost for this ability.</summary>
    public byte PointCost { get; set; }

    /// <summary>4 bytes trailing padding.</summary>
    [FixedLength(4)]
    public byte[] Padding8 { get; set; } = new byte[4];
}
