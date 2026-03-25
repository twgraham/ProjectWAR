using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_CHARACTER_INFO</c> (0xBE) subtype 1 — ability list.
/// <para>
/// Sends the player's available abilities and their effective mastery levels.
/// Wire format: <c>subtype(1) | count(1) | flags(2) | entries[count × (entry:u16, masteryLevel:u8)]</c>
/// </para>
/// </summary>
public class AbilityListResponse
{
    /// <summary>Subtype = 1 (ability levels).</summary>
    public byte SubType { get; set; } = 1;

    /// <summary>Number of ability entries.</summary>
    public byte Count { get; set; }

    /// <summary>Flags (always 0x0300 in V1).</summary>
    public ushort Flags { get; set; } = 0x0300;

    /// <summary>The ability entries.</summary>
    public AbilityLevelEntry[] Entries { get; set; } = [];
}

/// <summary>
/// A single entry in the ability list: ability ID + effective mastery level.
/// </summary>
public class AbilityLevelEntry
{
    /// <summary>Ability entry ID.</summary>
    public ushort Entry { get; set; }

    /// <summary>Effective mastery level for this ability's tree (or player level if core).</summary>
    public byte MasteryLevel { get; set; }
}
