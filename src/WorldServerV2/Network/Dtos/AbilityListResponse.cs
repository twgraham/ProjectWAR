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

    /// <summary>The ability entries.</summary>
    [SizedEntry(littleEndian: true)]
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
