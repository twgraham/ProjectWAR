using Core.Domain.Entities;
using Core.GameWorld.Components;
using Core.GameWorld.DataStore;
using Core.GameWorld.DataStore.Models;
using Core.GameWorld.Entities;
using Core.GameWorld.Spatial;
using Core.GameWorld.Stats;

namespace Core.GameWorld.Spawning;

/// <summary>
/// Concrete <see cref="IEntityFactory"/> implementation.
/// </summary>
public sealed class EntityFactory(IGameDataStore gameData) : IEntityFactory
{
    private readonly Random _rng = Random.Shared;

    // Career line IDs used by characterinfo_stats (matching V1 values)
    private const byte CareerShadowWarrior = 18;
    private const byte CareerIronBreaker   = 1;
    private const byte CareerSlayer        = 2;
    private const byte CareerSorcerer      = 24;
    private const byte CareerEngineer      = 4;
    private const byte CareerRunePriest    = 3;

    // ── IEntityFactory ───────────────────────────────────────────────────

    /// <inheritdoc />
    public CreatureEntity CreateCreature(SpawnDescriptor descriptor)
    {
        if (!gameData.Creatures.Protos.TryGetValue(descriptor.Entry, out var proto))
            throw new InvalidOperationException(
                $"No creature prototype found for entry {descriptor.Entry}.");

        var level   = descriptor.LevelOverride   ?? PickLevel(proto.MinLevel, proto.MaxLevel);
        var faction = descriptor.FactionOverride ?? proto.Faction;

        // Rank derived from faction using the same formula as V1 SetFaction():
        // Faction % 8 gives the local faction byte; Rank = localFaction / 2.
        byte localFaction = (byte)(faction % 8);
        byte rank = (byte)(localFaction / 2);

        // Initialize stats — produces real Wounds for health computation.
        uint maxHealth = InitCreatureStats(proto, level, rank, out var statContainer);

        var entity = new CreatureEntity(objectId: 0, proto, maxHealth)
        {
            Level   = level,
            Faction = faction,
            ModelId = (proto.Model2 != 0 && _rng.Next(2) == 0) ? proto.Model2 : proto.Model1,
            Scale   = PickScale(proto.MinScale, proto.MaxScale),
            Emote   = descriptor.EmoteOverride ?? proto.Emote,
        };

        // Transfer computed stats into the entity.
        CopyStats(statContainer, entity.Stats);
        entity.Stats.Flush();

        // BaseRadius: scale from proto data if available, otherwise scale the default.
        float scaleFactor = entity.Scale / 50f;
        entity.BaseRadius = proto.BaseRadiusUnits is > 0
            ? proto.BaseRadiusUnits.Value * scaleFactor / RegionConstants.UnitsPerFoot
            : RegionConstants.DefaultBaseRadiusFeet * scaleFactor;

        AttachCreatureComponents(entity, proto, descriptor);

        return entity;
    }

    /// <inheritdoc />
    public GameObjectEntity CreateGameObject(GameObjectSpawnDescriptor descriptor)
    {
        var entity = new GameObjectEntity(
            objectId:  0,
            descriptor: descriptor);

        if (descriptor.DoorId != 0)
        {
            entity.Attach(new DestructibleComponent(maxHealth: 100_000u, doorId: descriptor.DoorId));
        }

        return entity;
    }

    // ── Stat initialization ───────────────────────────────────────────────

    /// <summary>
    /// Computes the full stat set for a creature, mirroring V1's <c>SetCreatureStats()</c>.
    /// Returns the derived max-health (Wounds × 10).
    /// </summary>
    private uint InitCreatureStats(CreatureProto proto, byte level, byte rank, out StatContainer stats)
    {
        stats = new StatContainer();

        float statBonusMult = rank switch
        {
            1 => 2.25f,
            2 => 6f,
            3 => 12f,
            _ => 1.0f,
        };

        // ── Career base stats ─────────────────────────────────────────────
        byte careerLine = MapProtoCareerToCareerLine(proto.Career);
        byte clampedLevel = Math.Min(level, (byte)80);

        var baseStats = gameData.CareerStats.GetBaseStats(careerLine, clampedLevel);

        // Set base layer from the characterinfo_stats table
        foreach (var entry in baseStats)
            stats.SetBase(entry.Stat, entry.Value);

        // ── Career-specific primary stat bonus ────────────────────────────
        if (proto.Career == 0)
        {
            // Default career: add flat bonus to one primary stat (no power-stat overflow)
            if (proto.Ranged > 15)
                stats.SetItemBonus(StatId.BallisticSkill,
                    (int)(5 * statBonusMult * level * proto.PowerModifier));
            else
                stats.SetItemBonus(StatId.Strength,
                    (int)(5 * statBonusMult * level * proto.PowerModifier));

            // Also scale all secondary base stats by (statBonusMult - 1) as V1 does
            foreach (var entry in baseStats)
            {
                if (entry.Stat < StatId.BlockSkill && entry.Stat != StatId.Wounds)
                {
                    int extra = (int)(entry.Value * (statBonusMult - 1f) * proto.PowerModifier);
                    if (extra != 0)
                        stats.SetItemBonus(entry.Stat, stats[entry.Stat].ItemBonus + extra);
                }
            }
        }
        else
        {
            // Named career: apply per-stat multiplier with softcap overflow → power stat
            uint softcap = (uint)(50 + 25 * level);

            foreach (var entry in baseStats)
            {
                if (entry.Stat >= StatId.BlockSkill) continue;
                if (entry.Stat == StatId.Wounds) continue;
                if (entry.Stat == StatId.Agility) continue;

                int mult = GetCareerStatMultiplier(entry.Stat, proto.Career);
                int toAdd = (int)(mult * statBonusMult * level * proto.PowerModifier);
                int total = entry.Value + toAdd;

                StatId? powerStat = GetPowerStatFor(entry.Stat);
                if (total > (int)softcap && powerStat.HasValue)
                {
                    int overflow = total - (int)softcap;
                    stats.SetBase(entry.Stat, (int)softcap);
                    stats.SetItemBonus(powerStat.Value,
                        stats[powerStat.Value].ItemBonus + overflow);
                }
                else
                {
                    stats.SetItemBonus(entry.Stat,
                        stats[entry.Stat].ItemBonus + toAdd);
                }
            }
        }

        // ── Armor & resistances ───────────────────────────────────────────
        stats.SetItemBonus(StatId.Armor,
            (int)((36 + 13 * rank * proto.PowerModifier) * level));
        stats.SetItemBonus(StatId.SpiritResistance,
            (int)((7.5f + 2.5f * rank) * level * proto.PowerModifier));
        stats.SetItemBonus(StatId.ElementalResistance,
            (int)((7.5f + 2.5f * rank) * level * proto.PowerModifier));
        stats.SetItemBonus(StatId.CorporealResistance,
            (int)((7.5f + 2.5f * rank) * level * proto.PowerModifier));

        // ── Wounds (drives MaxHealth) ─────────────────────────────────────
        stats.SetBase(StatId.Wounds, GenerateWounds(level, rank, proto.WoundsModifier));

        // ── Per-entry overrides from creature_stats table ─────────────────
        if (gameData.Creatures.StatOverrides.TryGetValue(proto.Entry, out var overrides))
        {
            foreach (var row in overrides)
            {
                var statId = (StatId)(byte)row.StatId;
                if (row.StatValue < 0)
                    stats.SetItemBonus(statId, stats[statId].ItemBonus + row.StatValue);
                else
                    stats.SetItemBonus(statId, stats[statId].ItemBonus + row.StatValue);
            }
        }

        // Compute MaxHealth before Flush so we can pass it to the constructor.
        int wounds = stats[StatId.Wounds].GetTotal(floorAtZero: true);
        return (uint)Math.Max(1, wounds * 10);
    }

    private static int GenerateWounds(byte level, byte rank, float woundsModifier)
    {
        float wounds = 70f * (level + level / 2);
        wounds = rank switch
        {
            1 => wounds * 2,
            2 => wounds * 8,
            3 => wounds * 16,
            _ => wounds,
        };
        return (int)(wounds / 10f * (woundsModifier > 0 ? woundsModifier : 1f));
    }

    private static byte MapProtoCareerToCareerLine(byte protoCareer) => protoCareer switch
    {
        1 => CareerIronBreaker,
        2 => CareerSlayer,
        3 => CareerSorcerer,
        4 => CareerEngineer,
        5 => CareerRunePriest,
        _ => CareerShadowWarrior, // 0 = default
    };

    private static int GetCareerStatMultiplier(StatId stat, byte career) => stat switch
    {
        StatId.Strength      when career == 1 => 3,  // tank
        StatId.Strength      when career == 2 => 6,  // mdps
        StatId.BallisticSkill when career == 4 => 6, // rdps
        StatId.Intelligence  when career == 3 => 6,  // magic rdps
        StatId.Intelligence  when career == 5 => 6,  // healer
        StatId.Toughness     when career == 1 => 2,  // tank
        _ => 1,
    };

    private static StatId? GetPowerStatFor(StatId stat) => stat switch
    {
        StatId.Strength       => StatId.MeleePower,
        StatId.BallisticSkill => StatId.RangedPower,
        StatId.Willpower      => StatId.HealingPower,
        StatId.Intelligence   => StatId.MagicPower,
        _ => null,
    };

    /// <summary>
    /// Copies all base and item-bonus layers from the temporary container into the entity's
    /// live container, avoiding unnecessary copies of the flush-only layers.
    /// </summary>
    private static void CopyStats(StatContainer src, StatContainer dst)
    {
        foreach (StatId stat in System.Enum.GetValues<StatId>())
        {
            var srcEntry = src[stat];
            if (srcEntry.Base != 0)
                dst.SetBase(stat, srcEntry.Base);
            if (srcEntry.ItemBonus != 0)
                dst.SetItemBonus(stat, srcEntry.ItemBonus);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private byte PickLevel(byte min, byte max)
        => min == max ? min : (byte)_rng.Next(min, max + 1);

    private ushort PickScale(ushort min, ushort max)
        => min == max ? min : (ushort)_rng.Next(min, max + 1);

    private void AttachCreatureComponents(CreatureEntity entity, CreatureProto proto, SpawnDescriptor descriptor)
    {
        // Equipment — visual items from creature_items table
        if (gameData.Creatures.Items.TryGetValue(proto.Entry, out var items))
            entity.Attach(new EquipmentComponent(items));

        _ = descriptor;
    }
}

