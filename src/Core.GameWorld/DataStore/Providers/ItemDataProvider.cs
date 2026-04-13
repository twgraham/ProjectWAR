using System.Collections.Frozen;
using Core.Domain;
using Core.Domain.Entities;
using Core.GameWorld.DataStore.Models;
using Core.GameWorld.Items;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Core.GameWorld.DataStore.Providers;

/// <summary>
/// Loads all item-related data from the World database via EF Core, parsing serialized
/// stat/effect/craft strings at startup into immutable <see cref="ItemDefinition"/> objects.
/// </summary>
public sealed class ItemDataProvider(
    IDbContextFactory<WorldDbContext> dbContextFactory,
    ILogger<ItemDataProvider> logger) : IDataProvider<ItemData>
{
    public async Task<ItemData> LoadAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();

        // ── Load and parse item definitions ─────────────────────────────
        var infos = await db.ItemInfos
            .AsNoTracking()
            .ToListAsync();

        var definitions = new Dictionary<uint, ItemDefinition>(infos.Count);
        foreach (var info in infos)
        {
            var def = new ItemDefinition
            {
                Entry = info.Entry,
                Name = info.Name ?? string.Empty,
                Description = info.Description ?? string.Empty,
                ModelId = info.ModelId,
                BaseColor1 = info.BaseColor1 ?? 0,
                BaseColor2 = info.BaseColor2 ?? 0,
                Type = info.Type,
                SlotId = info.SlotId,
                Rarity = info.Rarity,
                Career = info.Career,
                Race = info.Race,
                Skills = info.Skills,
                Bind = info.Bind,
                MinRank = info.MinRank,
                MinRenown = info.MinRenown,
                ObjectLevel = info.ObjectLevel,
                UniqueEquipped = info.UniqueEquipped ?? 0,
                Armor = info.Armor,
                Dps = info.Dps,
                Speed = info.Speed,
                TwoHanded = info.TwoHanded != 0,
                Stats = ParseStats(info.Stats, info.Entry),
                Effects = ParseEffects(info.Effects),
                Crafts = ParseCrafts(info.Crafts),
                TalismanSlots = info.TalismanSlots,
                SpellId = info.SpellId,
                ItemSetId = info.ItemSet ?? 0,
                MaxStack = info.MaxStack,
                SellPrice = info.SellPrice,
                Dyeable = (info.DyeAble ?? 0) != 0,
                Salvageable = (info.Salvageable ?? 0) != 0,
                IsSiege = info.IsSiege,
                StartQuest = info.StartQuest,
                Unk27 = info.Unk27 ?? new byte[27],
            };
            definitions[info.Entry] = def;
        }

        // ── Load and parse item sets ────────────────────────────────────
        var setInfos = await db.ItemSetInfos
            .AsNoTracking()
            .ToListAsync();

        var sets = new Dictionary<uint, ItemSetDefinition>(setInfos.Count);
        foreach (var setInfo in setInfos)
        {
            var setDef = ParseItemSet(setInfo);
            if (setDef != null)
                sets[setInfo.Entry] = setDef;
        }

        logger.LogInformation("Loaded {ItemCount} item definitions and {SetCount} item sets",
            definitions.Count, sets.Count);

        return new ItemData(
            Definitions: definitions.ToFrozenDictionary(),
            Sets: sets.ToFrozenDictionary());
    }

    /// <summary>
    /// Parses the stat string into a frozen dictionary.
    /// <para>
    /// Database entries use the format <c>"statId:value;statId:value;"</c>, but some rows
    /// contain extended four-field entries like <c>"statId:value:field3:field4;"</c>.
    /// Only the first two fields (stat ID and value) are used — the purpose of fields 3
    /// and 4 is unknown and they are silently ignored, matching V1 behavior.
    /// </para>
    /// Duplicate stat keys are summed (matching V1 behavior).
    /// </summary>
    internal FrozenDictionary<byte, ushort> ParseStats(string? statsString, uint itemEntry)
    {
        if (string.IsNullOrEmpty(statsString))
            return FrozenDictionary<byte, ushort>.Empty;

        var result = new Dictionary<byte, ushort>();
        foreach (var segment in statsString.AsSpan().EnumerateDelimited(';'))
        {
            if (segment.Length < 3) continue; // minimum "k:v"

            var firstColon = segment.IndexOf(':');
            if (firstColon < 0) continue;

            // Only parse the first two colon-separated fields; additional fields
            // (e.g. "32:6:0:0") are ignored — their purpose is unknown.
            var afterFirst = segment[(firstColon + 1)..];
            var secondColon = afterFirst.IndexOf(':');
            var valueSpan = secondColon >= 0 ? afterFirst[..secondColon] : afterFirst;

            if (!byte.TryParse(segment[..firstColon], out var statId) ||
                !ushort.TryParse(valueSpan, out var value))
            {
                logger.LogWarning("Malformed stat entry in item {Entry}: '{Segment}'",
                    itemEntry, segment.ToString());
                continue;
            }

            if (statId == 0 || value == 0) continue;

            if (result.TryGetValue(statId, out var existing))
                result[statId] = (ushort)(existing + value);
            else
                result[statId] = value;
        }

        return result.ToFrozenDictionary();
    }

    /// <summary>
    /// Parses the <c>"id;id;"</c> effects string into a ushort array.
    /// </summary>
    internal static ReadOnlyMemory<ushort> ParseEffects(string? effectsString)
    {
        if (string.IsNullOrEmpty(effectsString))
            return ReadOnlyMemory<ushort>.Empty;

        var list = new List<ushort>();
        foreach (var segment in effectsString.AsSpan().EnumerateDelimited(';'))
        {
            if (segment.Length == 0) continue;
            if (ushort.TryParse(segment, out var effectId))
                list.Add(effectId);
        }

        return list.ToArray();
    }

    /// <summary>
    /// Parses the <c>"key:val;key:val;"</c> crafts string.
    /// </summary>
    internal static ReadOnlyMemory<KeyValuePair<byte, ushort>> ParseCrafts(string? craftsString)
    {
        if (string.IsNullOrEmpty(craftsString))
            return ReadOnlyMemory<KeyValuePair<byte, ushort>>.Empty;

        var crafts = craftsString.Trim();
        if (crafts.Length == 0)
            return ReadOnlyMemory<KeyValuePair<byte, ushort>>.Empty;

        // Special case: single byte with no colon (matching V1 behavior)
        if (crafts.Length < 3 && !crafts.Contains(':'))
        {
            if (byte.TryParse(crafts, out var singleKey))
                return new[] { new KeyValuePair<byte, ushort>(singleKey, 0) };
            return ReadOnlyMemory<KeyValuePair<byte, ushort>>.Empty;
        }

        var list = new List<KeyValuePair<byte, ushort>>();
        foreach (var segment in crafts.AsSpan().EnumerateDelimited(';'))
        {
            if (segment.Length < 3) continue;

            var colonIdx = segment.IndexOf(':');
            if (colonIdx < 0) continue;

            if (!byte.TryParse(segment[..colonIdx], out var key) ||
                !ushort.TryParse(segment[(colonIdx + 1)..], out var value))
                continue;

            if (key == 0 || value == 0) continue;
            list.Add(new KeyValuePair<byte, ushort>(key, value));
        }

        return list.ToArray();
    }

    /// <summary>
    /// Parses an <see cref="ItemSetInfo"/> entity into an <see cref="ItemSetDefinition"/>.
    /// </summary>
    private ItemSetDefinition? ParseItemSet(ItemSetInfo setInfo)
    {
        // Parse items: "itemId:itemName|itemId:itemName|..."
        var items = new List<ItemSetMember>();
        if (!string.IsNullOrEmpty(setInfo.ItemsString))
        {
            foreach (var segment in setInfo.ItemsString.AsSpan().EnumerateDelimited('|'))
            {
                if (segment.Length == 0) continue;
                var colonIdx = segment.IndexOf(':');
                if (colonIdx < 0) continue;

                if (uint.TryParse(segment[..colonIdx], out var itemId))
                    items.Add(new ItemSetMember(itemId, segment[(colonIdx + 1)..].ToString()));
            }
        }

        // Parse bonuses: "key:values|key:values|..."
        var bonuses = new List<ItemSetBonus>();
        if (!string.IsNullOrEmpty(setInfo.BonusString))
        {
            foreach (var segment in setInfo.BonusString.AsSpan().EnumerateDelimited('|'))
            {
                if (segment.Length == 0) continue;
                var colonIdx = segment.IndexOf(':');
                if (colonIdx < 0) continue;

                if (!byte.TryParse(segment[..colonIdx], out var key)) continue;
                var valueStr = segment[(colonIdx + 1)..];

                if (key >= 80)
                {
                    // Spell bonus: "key:spellId"
                    if (ushort.TryParse(valueStr, out var spellId))
                    {
                        bonuses.Add(new ItemSetBonus
                        {
                            RawKey = key,
                            BonusType = ItemSetBonusType.Spell,
                            ItemsRequired = (byte)(key % 10),
                            SpellId = spellId,
                        });
                    }
                }
                else
                {
                    // Stat bonus: "key:statId,value,percentage"
                    var parts = valueStr.ToString().Split(',');
                    if (parts.Length < 2) continue;

                    if (byte.TryParse(parts[0], out var statId) &&
                        ushort.TryParse(parts[1], out var statValue))
                    {
                        var isPercentage = parts.Length > 2 && parts[2] == "1";
                        bonuses.Add(new ItemSetBonus
                        {
                            RawKey = key,
                            BonusType = ItemSetBonusType.Stat,
                            ItemsRequired = (byte)(key % 10 - 2),
                            StatId = statId,
                            StatValue = statValue,
                            IsPercentage = isPercentage,
                        });
                    }
                }
            }
        }

        return new ItemSetDefinition
        {
            Entry = setInfo.Entry,
            Name = setInfo.Name ?? string.Empty,
            BuffLevel = setInfo.Unk,
            Items = items.ToArray(),
            Bonuses = bonuses.ToArray(),
        };
    }
}

/// <summary>
/// Extension to enumerate <c>ReadOnlySpan&lt;char&gt;</c> segments delimited by a separator.
/// </summary>
internal static class SpanDelimiterExtensions
{
    public static SpanSplitEnumerator EnumerateDelimited(this ReadOnlySpan<char> span, char separator)
        => new(span, separator);

    public ref struct SpanSplitEnumerator
    {
        private ReadOnlySpan<char> _remaining;
        private readonly char _separator;
        private bool _started;

        public SpanSplitEnumerator(ReadOnlySpan<char> span, char separator)
        {
            _remaining = span;
            _separator = separator;
            _started = false;
            Current = default;
        }

        public ReadOnlySpan<char> Current { get; private set; }

        public SpanSplitEnumerator GetEnumerator() => this;

        public bool MoveNext()
        {
            if (!_started)
                _started = true;

            if (_remaining.Length == 0 && _started && Current.Length == 0)
                return false;

            var idx = _remaining.IndexOf(_separator);
            if (idx < 0)
            {
                if (_remaining.Length == 0) return false;
                Current = _remaining;
                _remaining = [];
                return true;
            }

            Current = _remaining[..idx];
            _remaining = _remaining[(idx + 1)..];
            return true;
        }
    }
}
