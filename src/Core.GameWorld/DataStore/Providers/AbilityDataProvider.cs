using System.Collections.Frozen;
using Core.Domain;
using Core.Domain.Entities;
using Core.GameWorld.Combat;
using Core.GameWorld.Combat.Abilities;
using Core.GameWorld.DataStore.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Core.GameWorld.DataStore.Providers;

/// <summary>
/// Loads ability definitions from the <c>abilities</c>, <c>ability_commands</c>,
/// and <c>ability_damage_heals</c> tables and builds an <see cref="AbilityData"/>
/// bundle with pre-indexed career lookups.
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

        // Load all three tables in parallel
        var rows = await db.AbilityInfos.AsNoTracking().ToListAsync();
        var commandRows = await db.AbilityCommands.AsNoTracking().ToListAsync();
        var damageRows = await db.AbilityDamageHeals.AsNoTracking().ToListAsync();

        // Index damage heals by (Entry, ParentCommandId, ParentCommandSequence)
        var damageByCommand = damageRows
            .GroupBy(d => (d.Entry, d.ParentCommandId, d.ParentCommandSequence))
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(d => d.Index).First()); // Take first index per command

        // Index ability commands by Entry and group by CommandId
        var commandsByEntry = commandRows
            .GroupBy(c => c.Entry)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Convert EF entities → AbilityDefinition
        var definitions = new Dictionary<ushort, AbilityDefinition>(rows.Count);
        foreach (var row in rows)
        {
            var entry = (ushort)row.Entry;

            // Build commands for this ability
            var commands = BuildCommands(entry, commandsByEntry, damageByCommand);

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
                Commands = commands,
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
                if (def is { MasteryTree: 0 or null } or { MasteryTree: not 0 or not null, PointCost: 0 })
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
            kvp => kvp.Value.OrderBy(x => x.MinimumRank).ToArray());

        var masteryByCareer = masteryLists.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.OrderBy(x => x.MasteryTree).ThenBy(x => x.PointCost).ToArray());

        var totalCommands = commandRows.Count;
        var totalDamage = damageRows.Count;
        logger.LogInformation(
            "Loaded {Total} abilities ({Commands} commands, {Damage} damage/heal entries), indexed across {CoreCareers} careers (core) and {MasteryCareers} careers (mastery)",
            definitions.Count, totalCommands, totalDamage, coreByCareer.Count, masteryByCareer.Count);

        return new AbilityData(
            definitions.ToFrozenDictionary(),
            coreByCareer,
            masteryByCareer);
    }

    /// <summary>
    /// Builds the ordered list of <see cref="AbilityCommandDefinition"/> for a single ability.
    /// Commands with <c>CommandSequence == 0</c> are master commands; those with
    /// <c>CommandSequence > 0</c> are chained sub-commands attached to the master.
    /// </summary>
    private static IReadOnlyList<AbilityCommandDefinition> BuildCommands(
        ushort entry,
        Dictionary<int, List<AbilityCommandEntity>> commandsByEntry,
        Dictionary<(int Entry, short CmdId, short CmdSeq), AbilityDamageHealEntity> damageByCommand)
    {
        if (!commandsByEntry.TryGetValue(entry, out var cmdEntities))
            return [];

        // Group by CommandId: sequence 0 = master, sequence > 0 = chained
        var byCommandId = cmdEntities
            .GroupBy(c => c.CommandId)
            .OrderBy(g => g.Key);

        var result = new List<AbilityCommandDefinition>();

        foreach (var group in byCommandId)
        {
            AbilityCommandEntity? masterRow = null;
            var chainedRows = new List<AbilityCommandEntity>();

            foreach (var row in group.OrderBy(r => r.CommandSequence))
            {
                if (row.CommandSequence == 0)
                    masterRow = row;
                else
                    chainedRows.Add(row);
            }

            // If no master row (sequence 0), skip this command group
            if (masterRow is null)
                continue;

            // Build chained sub-commands
            var chained = chainedRows.Count > 0
                ? chainedRows.Select(r => MapCommand(entry, r, damageByCommand)).ToArray()
                : Array.Empty<AbilityCommandDefinition>();

            var master = MapCommand(entry, masterRow, damageByCommand, chained);
            result.Add(master);
        }

        return result;
    }

    /// <summary>
    /// Maps a single <see cref="AbilityCommandEntity"/> row to an
    /// <see cref="AbilityCommandDefinition"/>, attaching damage data if found.
    /// </summary>
    private static AbilityCommandDefinition MapCommand(
        ushort entry,
        AbilityCommandEntity row,
        Dictionary<(int Entry, short CmdId, short CmdSeq), AbilityDamageHealEntity> damageByCommand,
        IReadOnlyList<AbilityCommandDefinition>? chained = null)
    {
        var damageKey = (row.Entry, row.CommandId, row.CommandSequence);
        damageByCommand.TryGetValue(damageKey, out var damageRow);

        return new AbilityCommandDefinition
        {
            CommandId = (byte)row.CommandId,
            CommandSequence = (byte)row.CommandSequence,
            EffectType = ParseEffectType(row.CommandName),
            TargetType = ParseTargetType(row.Target),
            AoESource = ParseTargetType(row.EffectSource),
            EffectRadius = (byte?)row.EffectRadius,
            EffectAngle = (byte?)row.EffectAngle,
            MaxTargets = (byte?)row.MaxTargets,
            PrimaryValue = row.PrimaryValue ?? 0,
            SecondaryValue = row.SecondaryValue ?? 0,
            AttackingStat = (byte?)row.AttackingStat,
            IsDelayedEffect = row.IsDelayedEffect != 0,
            FromAllTargets = row.FromAllTargets != 0,
            NoAutoUse = row.NoAutoUse != null && row.NoAutoUse != 0,
            Damage = damageRow is not null ? MapDamage(damageRow) : null,
            ChainedCommands = chained ?? [],
        };
    }

    /// <summary>
    /// Maps a <see cref="AbilityDamageHealEntity"/> to <see cref="DamageDefinition"/>.
    /// </summary>
    private static DamageDefinition MapDamage(AbilityDamageHealEntity row)
    {
        return new DamageDefinition
        {
            DisplayEntry = (ushort)(row.DisplayEntry ?? 0),
            MinDamage = (ushort)(row.MinDamage ?? 0),
            MaxDamage = (ushort)(row.MaxDamage ?? 0),
            DamageVariance = (ushort)(row.DamageVariance ?? 0),
            CastTimeDamageMult = row.CastTimeDamageMult ?? 1.5f,
            WeaponMod = ParseWeaponDamage(row.WeaponDamageFrom),
            WeaponDamageScale = row.WeaponDamageScale ?? 0f,
            StatUsed = (byte)(row.StatUsed ?? 0),
            StatDamageScale = row.StatDamageScale ?? 1f,
            PriStatMultiplier = row.PriStatMultiplier ?? 0f,
            DamageType = ParseDamageType(row.DamageType),
            NoCrits = (row.NoCrits ?? 0) != 0,
            Undefendable = (row.Undefendable ?? 0) != 0,
            ArmorResistPenFactor = row.ArmorResistPenFactor ?? 0f,
            HatredScale = row.HatredScale ?? 1f,
            HealHatredScale = row.HealHatredScale,
            ResourceBuild = row.ResourceBuild ?? 0,
        };
    }

    // ── String → enum mapping helpers ────────────────────────────────

    /// <summary>
    /// Maps the legacy <c>CommandName</c> string from the database to the typed
    /// <see cref="AbilityEffectType"/> enum. Names not yet in the enum are
    /// mapped to <see cref="AbilityEffectType.DealDamage"/> as a fallback.
    /// </summary>
    private static AbilityEffectType ParseEffectType(string? name) => name switch
    {
        "DealDamage" => AbilityEffectType.DealDamage,
        "MultipleDealDamage" => AbilityEffectType.MultipleDealDamage,
        "BounceDamage" => AbilityEffectType.BounceDamage,
        "Slay" => AbilityEffectType.Slay,
        "StealLife" => AbilityEffectType.StealLife,
        "FlankingShot" => AbilityEffectType.DealDamage,
        "SwellOfGloom" => AbilityEffectType.DealDamage,
        "Ram" => AbilityEffectType.DealDamage,

        "InvokeBuff" => AbilityEffectType.InvokeBuff,
        "InvokeBuffWithDuration" => AbilityEffectType.InvokeBuff,
        "InvokeAura" => AbilityEffectType.InvokeAura,
        "InvokeLinkedBuff" => AbilityEffectType.InvokeLinkedBuff,
        "InvokeBouncingBuff" => AbilityEffectType.InvokeBuff,
        "StackBuffByNearbyFoes" => AbilityEffectType.InvokeBuff,
        "InvokeGuard" => AbilityEffectType.InvokeBuff,
        "InvokeOathFriend" => AbilityEffectType.InvokeBuff,
        "InvokeOnYourGuard" => AbilityEffectType.InvokeBuff,
        "SetPetBuff" => AbilityEffectType.InvokeBuff,

        "PuntEnemy" => AbilityEffectType.Knockback,
        "PuntSelf" => AbilityEffectType.Knockback,
        "JumpbackSnare" => AbilityEffectType.Knockback,
        "Pull" => AbilityEffectType.Pull,
        "JumpTo" => AbilityEffectType.JumpTo,

        "CleanseCC" => AbilityEffectType.CleanseCC,
        "CleanseDebuffType" => AbilityEffectType.CleanseDebuffType,
        "ExclusiveCleanseDebuffType" => AbilityEffectType.CleanseDebuffType,
        "CleanseBuff" => AbilityEffectType.CleanseDebuffType,

        "Interrupt" => AbilityEffectType.Interrupt,
        "SummonPet" => AbilityEffectType.SummonPet,
        "SpawnMobInstance" => AbilityEffectType.SummonPet,

        "SetCareerRes" => AbilityEffectType.ModifyCareerResource,
        "ModifyCareerRes" => AbilityEffectType.ModifyCareerResource,
        "ModifyMorale" => AbilityEffectType.ModifyMorale,
        "ModifyAP" => AbilityEffectType.ModifyActionPoints,
        "ResourceToAP" => AbilityEffectType.ModifyActionPoints,
        "StealAP" => AbilityEffectType.ModifyActionPoints,

        "GroundedEffect" => AbilityEffectType.GroundEffect,
        "GroundAttack" => AbilityEffectType.GroundEffect,
        "CreateLandMine" => AbilityEffectType.CreateLandMine,

        // Commands that exist in the DB but don't have dedicated handlers yet.
        // Map to DealDamage as a safe no-op for now (they'll have no Damage data
        // so the executor will skip actual damage logic).
        _ => AbilityEffectType.DealDamage,
    };

    /// <summary>
    /// Maps the legacy <c>Target</c>/<c>EffectSource</c> string to <see cref="CommandTargetType"/>.
    /// </summary>
    private static CommandTargetType ParseTargetType(string? target) => target switch
    {
        "Caster" or "caster" => CommandTargetType.Caster,
        "Ally" or "ally" => CommandTargetType.Ally,
        "AllyOrSelf" or "allyorself" => CommandTargetType.AllyOrSelf,
        "Enemy" or "enemy" => CommandTargetType.Enemy,
        "CareerTarget" or "careertarget" => CommandTargetType.CareerTarget,
        "Host" or "host" => CommandTargetType.Host,
        "AllyOrCareerTarget" or "allyorcareertarget" => CommandTargetType.AllyOrCareerTarget,
        "Groupmates" or "groupmates" => CommandTargetType.Groupmates,
        "Group" or "group" => CommandTargetType.Group,
        "GroupedAlly" or "groupedally" => CommandTargetType.GroupedAlly,
        "WithinGroup" or "withingroup" => CommandTargetType.WithinGroup,
        "EventInstigator" or "eventinstigator" => CommandTargetType.EventInstigator,
        "Siege" or "siege" => CommandTargetType.Siege,
        "SiegeCannon" or "siegecannon" => CommandTargetType.SiegeCannon,
        "NpcAlly" or "npcally" => CommandTargetType.NpcAlly,
        _ => CommandTargetType.Enemy, // Default to enemy target
    };

    /// <summary>
    /// Maps the legacy <c>DamageType</c> string to <see cref="DamageType"/>.
    /// </summary>
    private static DamageType ParseDamageType(string? type) => type switch
    {
        "Physical" or "physical" => DamageType.Physical,
        "Spirit" or "spirit" or "Spiritual" or "spiritual" => DamageType.Spiritual,
        "Elemental" or "elemental" => DamageType.Elemental,
        "Corporeal" or "corporeal" => DamageType.Corporeal,
        "Healing" or "healing" or "Heal" or "heal" => DamageType.Healing,
        "RawHealing" or "rawhealing" => DamageType.RawHealing,
        "RawDamage" or "rawdamage" => DamageType.RawDamage,
        _ => DamageType.Physical,
    };

    /// <summary>
    /// Maps the legacy <c>WeaponDamageFrom</c> string to <see cref="WeaponDamageContribution"/>.
    /// </summary>
    private static WeaponDamageContribution ParseWeaponDamage(string? source) => source switch
    {
        "MainHand" or "mainhand" => WeaponDamageContribution.MainHand,
        "OffHand" or "offhand" => WeaponDamageContribution.OffHand,
        "Ranged" or "ranged" => WeaponDamageContribution.Ranged,
        "DualWield" or "dualwield" => WeaponDamageContribution.DualWield,
        "MainAndRanged" or "mainandranged" => WeaponDamageContribution.MainAndRanged,
        _ => WeaponDamageContribution.None,
    };

    private static AbilityOrigin DeriveOrigin(AbilityInfoEntity row)
    {
        // Category 24 = Morale abilities in V1
        if (row.Category == 24)
            return AbilityOrigin.Morale;
        return AbilityOrigin.Standard;
    }
}
