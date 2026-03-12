namespace WorldServerV2.Data.Entities;

/// <summary>
/// Item definition loaded from the <c>item_infos</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="WorldDbContext"/>.
/// </summary>
public sealed class ItemInfo
{
    public uint Entry { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public byte Type { get; set; }
    public byte Race { get; set; }
    public uint ModelId { get; set; }
    public ushort SlotId { get; set; }
    public byte Rarity { get; set; }
    public uint Career { get; set; }
    public uint Skills { get; set; }
    public byte Bind { get; set; }
    public ushort Armor { get; set; }
    public ushort SpellId { get; set; }
    public uint? ItemSet { get; set; }
    public ushort Dps { get; set; }
    public ushort Speed { get; set; }
    public byte MinRank { get; set; }
    public byte MinRenown { get; set; }
    public byte ObjectLevel { get; set; }
    public byte? UniqueEquipped { get; set; }
    public int StartQuest { get; set; }

    /// <summary>
    /// Serialized stat dictionary stored as <c>"key:val;key:val;"</c>.
    /// Use <see cref="ParsedStats"/> for the deserialized form.
    /// </summary>
    public string Stats { get; set; } = string.Empty;

    /// <summary>
    /// Serialized effect list stored as <c>"id;id;"</c>.
    /// </summary>
    public string? Effects { get; set; }

    /// <summary>
    /// Serialized craft list stored as <c>"key:val;key:val;"</c>.
    /// </summary>
    public string? Crafts { get; set; }

    public uint SellPrice { get; set; }

    /// <summary>
    /// Serialized required-item list stored as <c>"key:val;key:val;"</c>.
    /// </summary>
    public string? SellRequiredItems { get; set; }

    public byte TalismanSlots { get; set; }
    public ushort MaxStack { get; set; }
    public byte[]? Unk27 { get; set; } = new byte[27];
    public string ScriptName { get; set; } = string.Empty;
    public ushort TwoHanded { get; set; }
    public string? CraftResult { get; set; }
    public ushort? DyeAble { get; set; }
    public ushort? Salvageable { get; set; }
    public ushort? BaseColor1 { get; set; }
    public ushort? BaseColor2 { get; set; }
    public ushort? TokUnlock { get; set; }
    public ushort? TokUnlock2 { get; set; }
    public ushort IsSiege { get; set; }
}
