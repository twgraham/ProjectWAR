using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_CHARACTER_INFO</c> (0xBE) subtype 3 — skill list.
/// <para>
/// Sends the player's career line, race, trained skill bitmask, and rally point.
/// Mirrors V1's <c>Player.SendSkills()</c>.
/// Wire format: <c>subtype(1) | padding(3) | careerLine(1) | race(1) | skills(4 LE) | rallyPoint(2 BE)</c>
/// </para>
/// </summary>
public class SkillListResponse
{
    /// <summary>Subtype = 3 (skills).</summary>
    public byte SubType { get; set; } = 3;

    /// <summary>Three bytes of zero padding.</summary>
    [FixedLength(3)]
    public byte[] Padding { get; set; } = new byte[3];

    /// <summary>Character's career line (1–24).</summary>
    public byte CareerLine { get; set; }

    /// <summary>Character's race.</summary>
    public byte Race { get; set; }

    /// <summary>Trained skills bitmask. Written little-endian to match V1's <c>WriteUInt32R</c>.</summary>
    [LittleEndian]
    public uint Skills { get; set; }

    /// <summary>Rally point (spawn point) zone ID.</summary>
    public ushort RallyPoint { get; set; }
}
