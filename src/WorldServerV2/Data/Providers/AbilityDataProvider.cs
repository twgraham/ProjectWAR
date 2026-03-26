using System.Collections.Frozen;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using WorldServerV2.Data.Domain;
using WorldServerV2.World.Combat.Abilities;

namespace WorldServerV2.Data.Providers;

/// <summary>
/// Loads ability definitions from the <c>abilities</c> table and builds an
/// <see cref="AbilityData"/> bundle with pre-indexed career lookups.
/// </summary>
public class AbilityDataProvider(
    IDbContextFactory<WorldDbContext> dbContextFactory,
    ILogger<AbilityDataProvider> logger) : IDataProvider<AbilityData>
{
    /// <summary>Maximum number of career lines (1–24).</summary>
    private const int MaxCareerLines = 24;

    public async Task<AbilityData> LoadAsync()
    {
        await using var db = await dbContextFactory.CreateDbContextAsync();
        var rows = await db.AbilityInfos
            .AsNoTracking()
            .ToListAsync();

        // Convert EF entities → AbilityDefinition
        var definitions = new Dictionary<ushort, AbilityDefinition>(rows.Count);
        foreach (var row in rows)
        {
            var entry = (ushort)row.Entry;
            var def = new AbilityDefinition
            {
                Entry = entry,
                Name = row.Name ?? string.Empty,
                CareerLine = row.CareerLine,
                MasteryTree = (byte)(row.MasteryTree ?? 0),
                Category = (byte)(row.Category ?? 0),
                AbilityType = (AbilityType)(row.AbilityType ?? 0),
                Origin = DeriveOrigin(row),
                MinimumRank = (byte)(row.MinimumRank ?? 0),
                MinimumRenown = (byte)(row.MinimumRenown ?? 0),
                PointCost = (byte)(row.PointCost ?? 0),
                WeaponNeeded = (WeaponRequirement)(row.WeaponNeeded ?? 0),
                CastTime = (ushort)(row.CastTime ?? 0),
                Cooldown = (ushort)(row.Cooldown ?? 0),
                CooldownCap = (ushort)(row.CooldownCap ?? 0),
                CooldownEntry = (ushort)(row.CooldownEntry ?? 0),
                ApCost = (byte)(row.ApCost ?? 0),
                SpecialCost = row.SpecialCost ?? 0,
                CashCost = row.CashCost ?? 0,
                CanCastWhileMoving = (row.MoveCast ?? 0) != 0,
                IgnoreGlobalCooldown = (row.IgnoreGlobalCooldown ?? 0) != 0,
                IgnoreOwnModifiers = (row.IgnoreOwnModifiers ?? 0) != 0,
                Range = (ushort)(row.Range ?? 0),
                MinRange = (byte)(row.MinRange ?? 0),
            };

            definitions[entry] = def;
        }

        // Build per-career-line indexes using the career bitmask.
        // Bit (careerLine - 1) set → ability belongs to that career.
        var coreLists = new Dictionary<byte, List<AbilityDefinition>>();
        var masteryLists = new Dictionary<byte, List<AbilityDefinition>>();

        foreach (var def in definitions.Values)
        {
            if (def.CareerLine is 0 or null)
                continue; // shared/global abilities — skip career indexing

            for (var cl = 1; cl <= MaxCareerLines; cl++)
            {
                if ((def.CareerLine & (1u << (cl - 1))) == 0)
                    continue;

                var key = (byte)cl;
                if (def.MasteryTree is 0 or null)
                {
                    if (!coreLists.TryGetValue(key, out var list))
                        coreLists[key] = list = [];
                    list.Add(def);
                }
                else
                {
                    if (!masteryLists.TryGetValue(key, out var list))
                        masteryLists[key] = list = [];
                    list.Add(def);
                }
            }
        }

        // Sort core abilities by MinimumRank, mastery by tree then PointCost
        var coreByCareer = coreLists.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp =>
            {
                kvp.Value.Sort((a, b) => a.MinimumRank.CompareTo(b.MinimumRank));
                return kvp.Value.ToArray();
            });

        var masteryByCareer = masteryLists.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.OrderBy(x => x.MasteryTree).ThenBy(x => x.PointCost).ToArray());

        logger.LogInformation(
            "Loaded {Total} abilities, indexed across {CoreCareers} careers (core) and {MasteryCareers} careers (mastery)",
            definitions.Count, coreByCareer.Count, masteryByCareer.Count);

        return new AbilityData(
            definitions.ToFrozenDictionary(),
            coreByCareer,
            masteryByCareer);
    }

    private static AbilityOrigin DeriveOrigin(Entities.AbilityInfoEntity row)
    {
        // Category 24 = Morale abilities in V1
        if (row.Category == 24)
            return AbilityOrigin.Morale;
        return AbilityOrigin.Standard;
    }
}
