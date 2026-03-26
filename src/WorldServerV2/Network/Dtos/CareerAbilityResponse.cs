using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// Server response for <c>F_CAREER_PACKAGE_INFO</c> (0xF3) — core career ability or tactic entry.
/// <para>
/// Sent once per ability slot (treeId=0) or tactic slot (treeId=1).
/// Populates the client's career browser with ability icons, names, and unlock levels.
/// </para>
/// <para>
/// <b>Wire format:</b> 24-byte fixed header + variable stream data containing one
/// ability-reference item, 16 bytes of visual data, a pascal-encoded name, and trailing zeros.
/// The client reads visual data from the stream but overwrites it with local .myp data
/// using <see cref="ReferenceId"/> for lookup, so placeholder visual bytes are safe.
/// </para>
/// <para>
/// Decoded from IDA disassembly of the WAR client's <c>sub_51022A</c> handler,
/// validated against 6 sniff capture files covering all 24 careers.
/// </para>
/// </summary>
public class CareerAbilityResponse
{
    // ── Fixed header (24 bytes) ────────────────────────────────────

    /// <summary>Tree identifier: 0 = abilities, 1 = tactics.</summary>
    public byte TreeId { get; set; }

    /// <summary>Sub-category (always 1 for career entries).</summary>
    public byte SubCategory { get; set; } = 1;

    /// <summary>1-based slot position within the tree (uint16 BE).</summary>
    public ushort EntryIndex { get; set; }

    /// <summary>Mastery level (always 0 for core career abilities).</summary>
    public byte MasteryLevel { get; set; }

    /// <summary>Minimum rank required for ability.</summary>
    public byte MinimumRank { get; set; }

    /// <summary>3 reserved bytes (always 0 for core career abilities).</summary>
    [FixedLength(3)]
    public byte[] Reserved1 { get; set; } = new byte[3];

    /// <summary>
    /// Optional data flags. Bit 0 = has uint32 value. Always 0x01 for career entries.
    /// </summary>
    public byte OptionalFlags { get; set; } = 1;

    /// <summary>2 bytes padding.</summary>
    [FixedLength(2)]
    public byte[] Padding1 { get; set; } = new byte[2];

    /// <summary>Reserved uint32 (always 0 for core career abilities).</summary>
    public uint HeaderReserved1 { get; set; }

    /// <summary>Reserved uint32 (always 0 for core career abilities).</summary>
    public uint HeaderReserved2 { get; set; }

    /// <summary>Reserved uint32 (always 0 for core career abilities).</summary>
    public uint HeaderReserved3 { get; set; }

    // ── Stream data ────────────────────────────────────────────────
    
    public uint CashCost { get; set; }

    /// <summary>Number of items in this package (always 1 for career abilities).</summary>
    public byte ItemCount { get; set; } = 1;

    /// <summary>Ability entry ID from the <c>abilities</c> table (uint32 BE).</summary>
    public uint PackageId { get; set; }

    /// <summary>Item type: 2 = ability reference (client looks up locally).</summary>
    public byte ItemType { get; set; } = 2;

    /// <summary>
    /// Client-internal ability ID for local icon/name/data lookup.
    /// Computed as <c>abilityEntry + racialPairOffset</c> where the offset
    /// is determined by the career's racial pairing (Dwarves/Greenskins: −1399,
    /// Empire/Chaos: +4251, High Elves/Dark Elves: +4351).
    /// </summary>
    public uint ReferenceId { get; set; }

    /// <summary>Item flag 1 (always 0).</summary>
    public byte Flag1 { get; set; }

    /// <summary>Item flag 2 (always 1).</summary>
    public byte Flag2 { get; set; } = 1;

    /// <summary>
    /// 16 bytes of visual/classification data read by the client's <c>sub_4F8294</c>.
    /// The client immediately overrides this with data from local .myp files using
    /// <see cref="ReferenceId"/> via <c>sub_51FE02</c> → <c>sub_4F8052</c>.
    /// Safe to leave as zeros when ReferenceId is correct.
    /// </summary>
    [FixedLength(16)]
    public byte[] VisualData { get; set; } = new byte[16];

    /// <summary>Ability display name (pascal-encoded).</summary>
    [PascalString]
    public string AbilityName { get; set; } = string.Empty;

    /// <summary>Number of buff entries following the name (always 0 for career abilities).</summary>
    public byte BuffCount { get; set; }

    /// <summary>4 trailing zero bytes.</summary>
    [FixedLength(4)]
    public byte[] Trailing { get; set; } = new byte[4];
}
