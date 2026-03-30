using System.Collections.Frozen;
using Core.Infrastructure.Network.Serialization.Attributes;
using WorldServerV2.Data.Domain;
using WorldServerV2.World.Combat.Abilities;
using WorldServerV2.World.Items;
using WorldServerV2.World.Stats;

namespace WorldServerV2.Network.Dtos;

public class GetItemResponse
{
    // The size of the items list is determined by the first byte only, meaning the maximum number of items that can be
    // sent in a single packet is 255. The PacketLength attribute is set to 4 to account for extra flags. Offset 1 and 3
    // are never accessed by the client. Offset 2 is used for flags of unknown purpose.
    [PacketLength(4, LittleEndian = true)]
    public List<ItemEntry> Items
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Count, 255);
            field = value;
        }
    } = [];
}

public class ItemEntry
{
    public ushort SlotId { get; set; }
    
    [NullPrefixed]
    public PreAppearance? PreAppearance { get; set; }
    
    public uint Entry { get; set; }
    
    // Client behavior is conditional on Entry: if Entry is 0, the client will ignore ItemData.
    public ItemData? Data { get; set; }

    /// <summary>
    /// Creates an <see cref="ItemEntry"/> from a live inventory item.
    /// </summary>
    /// <param name="item">The live item instance.</param>
    /// <param name="itemDefs">Item definition lookup (for alt-appearance resolution).</param>
    /// <param name="abilityDefs">Ability definition lookup (for spell cooldown times).</param>
    public static ItemEntry FromItem(
        Item item,
        FrozenDictionary<uint, ItemDefinition> itemDefs,
        FrozenDictionary<ushort, AbilityDefinition> abilityDefs)
    {
        var info = item.Info;

        return new ItemEntry
        {
            SlotId = item.SlotId,
            PreAppearance = null, // Only set for repairable items (entry 2500000–2600000 range)
            Entry = item.Entry,
            Data = BuildItemData(item, info, itemDefs, abilityDefs),
        };
    }

    private static ItemData BuildItemData(
        Item item,
        ItemDefinition info,
        FrozenDictionary<uint, ItemDefinition> itemDefs,
        FrozenDictionary<ushort, AbilityDefinition> abilityDefs)
    {
        // ── Alt appearance ───────────────────────────────────────────────
        var (altModelId, altEntry, altName) = ResolveAltAppearance(item, info, itemDefs);

        // ── Trophy / Enhancement conditional Career field ────────────────
        uint career = info.Career;
        if (item.SlotId != 0 && info.Type == 24) // Trophy: encode alt appearance entry
            career = item.AlternateAppearanceEntry;

        // ── Stats ────────────────────────────────────────────────────
        var stats = new ItemStat[info.Stats.Count];
        var i = 0;
        foreach (var kv in info.Stats)
        {
            stats[i++] = new ItemStat
            {
                StatId = kv.Key,
                Value = kv.Value,
                IsExpiring = kv.Key == (byte)StatId.AutoAttackSpeed,
                TimeRemaining = kv.Key == (byte)StatId.AutoAttackSpeed ? 1u : 0u,
            };
        }

        // ── Effects ──────────────────────────────────────────────────
        var effectSpan = info.Effects.Span;
        var effects = new ItemEffect[effectSpan.Length];
        for (var e = 0; e < effectSpan.Length; e++)
            effects[e] = new ItemEffect { EffectId = effectSpan[e] };

        // ── Spells ───────────────────────────────────────────────────
        ItemSpell[] spells;
        if (info.SpellId == 0)
        {
            spells = [];
        }
        else
        {
            ushort cooldown = 0;
            if (abilityDefs.TryGetValue(info.SpellId, out var abilityDef))
                cooldown = abilityDef.Cooldown;

            spells =
            [
                new ItemSpell
                {
                    SpellId = info.SpellId,
                    Cooldown = cooldown,
                    TimeRemaining = 0, // No active cooldown at init
                }
            ];
        }

        // ── Crafting ─────────────────────────────────────────────────
        ItemCrafting[] crafting;
        if (info.Type == 23) // Talisman/Enhancement — V1 writes 0 count
        {
            crafting = [];
        }
        else
        {
            var craftSpan = info.Crafts.Span;
            crafting = new ItemCrafting[craftSpan.Length];
            for (var c = 0; c < craftSpan.Length; c++)
                crafting[c] = new ItemCrafting { CraftKey = craftSpan[c].Key, CraftValue = craftSpan[c].Value };
        }

        // ── Talismans ────────────────────────────────────────────────
        var talismans = BuildTalismans(item, info, itemDefs, abilityDefs);

        // ── Flags (from Unk27[0..8] with computed overrides) ─────────────
        var unk = info.Unk27;
        byte flags1 = unk.Length > 6 ? unk[6] : (byte)0;
        if (info.Dyeable) flags1 |= 1;
        if (info.Salvageable) flags1 |= 2;

        byte bindFlag = 0;
        if (info.Bind == 1 && !item.BoundToPlayer)
            bindFlag = 4; // BoP, not yet bound
        else if (info.Bind == 2 && !item.BoundToPlayer)
            bindFlag = 8; // BoE, not yet bound

        bool boundFlag = info.Bind == 2 && item.BoundToPlayer;

        // ── Tail (from Unk27[13..26]) ────────────────────────────────────
        var tailField1 = ReadUshortOrZero(unk, 13);
        var tailField2 = unk.Length > 15 ? unk[15] : (byte)0;
        var tailField3 = ReadUintOrZero(unk, 16);
        // Unk27[20] is the NullPrefixed flag position for GuildHeraldry — always 0 for non-guild
        var tailField4 = ReadUintOrZero(unk, 21);
        var tailField5 = unk.Length > 25 && unk[25] != 0;
        var tailField6 = unk.Length > 26 && unk[26] != 0;

        // Apply TwoHanded flag into tail
        if (info.TwoHanded && unk.Length > 26)
            tailField6 = (unk[26] | 1) != 0;

        return new ItemData
        {
            ModelId = (ushort)info.ModelId,
            AltModelId = altModelId,
            AltEntry = altEntry,
            AltName = altName,
            EquipSlotId = info.SlotId,
            Type = info.Type,
            MinRank = info.MinRank,
            ObjectLevel = info.ObjectLevel,
            MinRenown = info.MinRenown,
            MinRenown2 = info.MinRenown,
            UniqueEquipped = info.UniqueEquipped,
            Rarity = info.Rarity,
            Bind = info.Bind,
            Race = info.Race,
            Career = career,
            TypeBitmask = 0, // Conditional — only written for types 23/24
            TrophyByte1 = 0,
            TrophyByte2 = 0,
            BaseColor1 = info.BaseColor1,
            BaseColor2 = info.BaseColor2,
            SellPrice = info.SellPrice,
            MaxStack = info.MaxStack,
            Count = item.Count > 0 ? item.Count : (ushort)1,
            ItemSetId = info.ItemSetId,
            Skills = info.Skills,
            DpsOrArmour = info.Dps > 0 ? info.Dps : info.Armor,
            Speed = info.Speed,
            Name = info.Name,
            Stats = stats,
            Effects = effects,
            Spells = spells,
            Crafting = crafting,
            Mystery = null, // V1 always writes 0 here
            Talismans = talismans,
            Description = info.Description,
            Unk1 = unk.Length > 0 && unk[0] != 0,
            Unk2 = unk.Length > 1 && unk[1] != 0,
            Unk3 = unk.Length > 2 ? unk[2] : (byte)0,
            Unk4 = unk.Length > 3 ? unk[3] : (byte)0,
            FlagCount = unk.Length > 4 ? unk[4] : (byte)0,
            BindFlag = bindFlag,
            Flags1 = flags1,
            Unk5 = unk.Length > 7 ? unk[7] : (byte)0,
            BoundFlag = boundFlag,
            PrimaryDye = item.PrimaryDye,
            SecondaryDye = item.SecondaryDye,
            TailField1 = tailField1,
            TailField2 = tailField2,
            TailField3 = tailField3,
            GuildHeraldry = null, // TODO: wire when guild system is available
            TailField4 = tailField4,
            TailField5 = tailField5,
            TailField6 = tailField6,
        };
    }

    private static (ushort ModelId, uint Entry, string Name) ResolveAltAppearance(
        Item item, ItemDefinition info, FrozenDictionary<uint, ItemDefinition> itemDefs)
    {
        if (item.AlternateAppearanceEntry == 0 || info.Type == 24)
            return (0, 0, "");

        if (!itemDefs.TryGetValue(item.AlternateAppearanceEntry, out var altInfo))
            return (0, 0, "");

        return ((ushort)altInfo.ModelId, item.AlternateAppearanceEntry, altInfo.Name);
    }

    private static ItemTalisman[] BuildTalismans(
        Item item, ItemDefinition info,
        FrozenDictionary<uint, ItemDefinition> itemDefs,
        FrozenDictionary<ushort, AbilityDefinition> abilityDefs)
    {
        if (info.TalismanSlots == 0)
            return [];

        var talismans = new ItemTalisman[info.TalismanSlots];
        for (var slot = 0; slot < info.TalismanSlots; slot++)
        {
            var talis = slot < item.Talismans.Length ? item.Talismans[slot] : null;

            if (talis == null || !itemDefs.TryGetValue(talis.Entry, out var talisInfo))
            {
                // Empty talisman slot — V1 writes just Entry=0 (4 bytes).
                // The DTO will serialize all fields; Entry=0 signals empty.
                talismans[slot] = new ItemTalisman { Entry = 0, Name = "" };
                continue;
            }

            // Talisman stats
            var tStats = new ItemStat[talisInfo.Stats.Count];
            var si = 0;
            foreach (var kv in talisInfo.Stats)
            {
                tStats[si++] = new ItemStat
                {
                    StatId = kv.Key,
                    Value = kv.Value,
                    TimeRemaining = talis.Timer,
                };
            }

            // Talisman effects
            var tEffectSpan = talisInfo.Effects.Span;
            var tEffects = new ItemEffect[tEffectSpan.Length];
            for (var e = 0; e < tEffectSpan.Length; e++)
                tEffects[e] = new ItemEffect { EffectId = tEffectSpan[e] };

            talismans[slot] = new ItemTalisman
            {
                Entry = talis.Entry,
                ModelId = (ushort)talisInfo.ModelId,
                Name = talisInfo.Name,
                Stats = tStats,
                Effects = tEffects,
                Crafting = null,
                Mystery = null,
            };
        }

        return talismans;
    }

    private static ushort ReadUshortOrZero(byte[] data, int offset)
    {
        if (offset + 1 >= data.Length) return 0;
        return (ushort)((data[offset] << 8) | data[offset + 1]);
    }

    private static uint ReadUintOrZero(byte[] data, int offset)
    {
        if (offset + 3 >= data.Length) return 0;
        return (uint)((data[offset] << 24) | (data[offset + 1] << 16) |
                       (data[offset + 2] << 8) | data[offset + 3]);
    }
}

public class PreAppearance
{
    [PascalString]
    public string AltName { get; set; } = "";
    public uint AltEntry { get; set; }
    public uint Unk1 { get; set; }
    public uint Unk2 { get; set; }
}

public class ItemData
{
    public ushort ModelId { get; set; }
    public ushort AltModelId { get; set; }
    public uint AltEntry { get; set; }
    [PascalString]
    public string AltName { get; set; } = "";
    
    public ushort EquipSlotId { get; set; }
    public byte Type { get; set; }
    public byte MinRank { get; set; }
    public byte ObjectLevel { get; set; }
    public byte MinRenown { get; set; }
    public byte MinRenown2 { get; set; }
    public byte UniqueEquipped { get; set; } // Added after client investigation.. sus...
    public byte Rarity { get; set; }
    public byte Bind { get; set; }
    public byte Race { get; set; }
    public uint Career { get; set; }
    
    [ConditionalOn(nameof(Type), 23, 24)]
    public uint TypeBitmask { get; set; }

    [ConditionalOn(nameof(Type), 24)]
    public byte TrophyByte1 { get; set; }

    [ConditionalOn(nameof(Type), 24)]
    public byte TrophyByte2 { get; set; }
    
    public ushort BaseColor1 { get; set; }
    public ushort BaseColor2 { get; set; }
    public uint SellPrice { get; set; }
    public ushort MaxStack { get; set; }
    public ushort Count { get; set; }
    public uint ItemSetId { get; set; }
    public uint Skills { get; set; }
    public ushort DpsOrArmour { get; set; }
    public ushort Speed { get; set; }
    
    [PascalString]
    public string Name { get; set; } = "";
    
    [PacketLength(1)]
    public ItemStat[] Stats { get; set; } = [];
    
    [PacketLength(1)]
    public ItemEffect[] Effects { get; set; } = [];
    
    [PacketLength(1)]
    public ItemSpell[] Spells { get; set; } = [];

    [PacketLength(1)]
    public ItemCrafting[] Crafting { get; set; } = [];
    
    [NullPrefixed]
    public ItemMystery? Mystery { get; set; }
    
    [PacketLength(1)]
    public ItemTalisman[] Talismans { get; set; } = [];
    
    [PascalString]
    public string Description { get; set; } = "";
    
    // Flags
    public bool Unk1 { get; set; }
    public bool Unk2 { get; set; }
    public byte Unk3 { get; set; }
    public byte Unk4 { get; set; }
    
    public byte FlagCount { get; set; }
    public byte BindFlag { get; set; }
    public byte Flags1 { get; set; } // Dyeable, Salvageable
    public byte Unk5 { get; set; }
    public bool BoundFlag { get; set; }
    
    public ushort PrimaryDye { get; set; }
    public ushort SecondaryDye { get; set; }
    
    public ushort TailField1 { get; set; }
    public byte TailField2 { get; set; }
    public uint TailField3 { get; set; }
    
    [NullPrefixed]
    public GuildHeraldry? GuildHeraldry { get; set; }
    
    public uint TailField4 { get; set; }
    public bool TailField5 { get; set; }
    public bool TailField6 { get; set; }
}

public class ItemStat
{
    public byte StatId { get; set; }
    public ushort Value { get; set; }
    public bool IsExpiring { get; set; }
    public uint TimeRemaining { get; set; }
}

public class ItemEffect
{
    public ushort EffectId { get; set; }
    public uint TimeRemaining { get; set; }
}

public class ItemCrafting
{
    public byte CraftKey { get; set; }
    public ushort CraftValue { get; set; }
}

public class ItemTalisman
{
    public uint Entry { get; set; }
    public ushort ModelId { get; set; }
    [PascalString]
    public string Name { get; set; } = "";
    
    [PacketLength(1)]
    public ItemStat[] Stats { get; set; } = [];
    
    [PacketLength(1)]
    public ItemEffect[] Effects { get; set; } = [];
    
    [PacketLength(1)]
    public ItemCrafting[]? Crafting { get; set; }
    
    [NullPrefixed]
    public TalismanMystery? Mystery { get; set; }
}

public class ItemSpell
{
    public uint SpellId { get; set; }
    public ushort Cooldown { get; set; }
    public ushort TimeRemaining { get; set; }
}

public class ItemMystery
{
    public ushort Unk1 { get; set; }
    public ushort Unk2 { get; set; }
}

public class TalismanMystery
{
    public byte Unk1 { get; set; }
    public byte Unk2 { get; set; }
}

public class GuildHeraldry
{
    public ushort Emblem { get; set; }
    public ushort Pattern { get; set; }
    public byte Color1 { get; set; }
    public byte Color2 { get; set; }
    public byte Discarded { get; set; }
    public byte Shape { get; set; }
    public byte Extra { get; set; }
}