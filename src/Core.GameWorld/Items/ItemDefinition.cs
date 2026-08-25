using System.Collections.Frozen;

namespace Core.GameWorld.Items;

/// <summary>
/// Immutable definition of an item loaded from the database (via GameDataStore).
/// Replaces V1's mutable <c>Item_Info</c> with pre-parsed stat/effect/craft collections.
/// <para>
/// Per-instance mutable state (count, dyes, talismans, binding) lives in <see cref="Item"/>,
/// which holds a reference back to this definition.
/// </para>
/// </summary>
public sealed class ItemDefinition
{
    // ── Identity ─────────────────────────────────────────────────────

    public required uint Entry { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }

    // ── Visual ───────────────────────────────────────────────────────

    public uint ModelId { get; init; }
    public ushort BaseColor1 { get; init; }
    public ushort BaseColor2 { get; init; }

    // ── Classification ───────────────────────────────────────────────

    /// <summary>Item type: weapon, armor, trophy, crafting, talisman, etc.</summary>
    public byte Type { get; init; }

    /// <summary>Equipment slot this item can occupy (wire-format slot).</summary>
    public ushort SlotId { get; init; }

    /// <summary>Rarity tier (0=white, 1=green, 2=blue, 3=purple, 4=gold, 5=orange).</summary>
    public byte Rarity { get; init; }

    /// <summary>Career bitmask — which careers can equip this item.</summary>
    public uint Career { get; init; }

    /// <summary>Race restriction bitmask.</summary>
    public byte Race { get; init; }

    /// <summary>Skill requirements bitmask.</summary>
    public uint Skills { get; init; }

    /// <summary>Binding type: 0=none, 1=BoE (bind on equip), 2=BoP (bind on pickup).</summary>
    public byte Bind { get; init; }

    // ── Requirements ─────────────────────────────────────────────────

    public byte MinRank { get; init; }
    public byte MinRenown { get; init; }
    public byte ObjectLevel { get; init; }
    public byte UniqueEquipped { get; init; }

    // ── Combat Stats ─────────────────────────────────────────────────

    /// <summary>Armor value (for armor pieces).</summary>
    public ushort Armor { get; init; }

    /// <summary>Damage per second (for weapons).</summary>
    public ushort Dps { get; init; }

    /// <summary>Attack speed (for weapons).</summary>
    public ushort Speed { get; init; }

    /// <summary>Whether this is a two-handed weapon.</summary>
    public bool TwoHanded { get; init; }

    /// <summary>Whether this item is a shield (Type == 5, matching V1's ITEMTYPES_SHIELD).</summary>
    public bool IsShield => Type == 5;

    // ── Bonus Stats ──────────────────────────────────────────────────

    /// <summary>
    /// Pre-parsed stat bonuses. Key = stat ID (byte), Value = bonus value (ushort).
    /// Parsed at startup from the <c>"key:val;key:val;"</c> DB string.
    /// </summary>
    public required FrozenDictionary<byte, ushort> Stats { get; init; }

    /// <summary>
    /// Pre-parsed visual/proc effect IDs. Parsed from <c>"id;id;"</c> DB string.
    /// </summary>
    public required ReadOnlyMemory<ushort> Effects { get; init; }

    /// <summary>
    /// Pre-parsed craft recipe entries. Key = craft type, Value = recipe ID.
    /// Parsed from <c>"key:val;key:val;"</c> DB string.
    /// </summary>
    public required ReadOnlyMemory<KeyValuePair<byte, ushort>> Crafts { get; init; }

    // ── Talisman / Enhancement ───────────────────────────────────────

    /// <summary>Number of talisman slots available on this item (0–3).</summary>
    public byte TalismanSlots { get; init; }

    // ── Spell / Use-Effect ───────────────────────────────────────────

    /// <summary>On-use spell/proc ability entry. 0 = none.</summary>
    public ushort SpellId { get; init; }

    // ── Item Set ─────────────────────────────────────────────────────

    /// <summary>Item set entry ID, or 0 if not part of a set.</summary>
    public uint ItemSetId { get; init; }

    // ── Stacking / Economy ───────────────────────────────────────────

    public ushort MaxStack { get; init; }
    public uint SellPrice { get; init; }

    // ── Flags / Misc ─────────────────────────────────────────────────

    /// <summary>Whether this item can be dyed.</summary>
    public bool Dyeable { get; init; }

    /// <summary>Whether this item can be salvaged.</summary>
    public bool Salvageable { get; init; }

    /// <summary>Siege engine flag.</summary>
    public ushort IsSiege { get; init; }

    /// <summary>Start quest entry triggered on acquisition. 0 = none.</summary>
    public int StartQuest { get; init; }

    /// <summary>
    /// Raw 27-byte unknown/flag array from the DB, used during packet serialization.
    /// Contains dyeable/salvageable/twoHanded bits and other client-expected flags.
    /// </summary>
    public required byte[] Unk27 { get; init; }
}
