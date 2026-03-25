using WorldServerV2.Data.Domain;
using WorldServerV2.World.Abilities;
using WorldServerV2.World.Combat.Abilities;

namespace WorldServerV2.Services;

/// <summary>
/// Resolves a player's active ability list from their career, level, and mastery state.
/// <para>
/// Pure computation — no side effects, no state. Depends only on the static
/// <see cref="AbilityData"/> from the game data store.
/// </para>
/// </summary>
public sealed class AbilityResolver
{
    private readonly AbilityData _abilityData;

    public AbilityResolver(AbilityData abilityData)
    {
        _abilityData = abilityData ?? throw new ArgumentNullException(nameof(abilityData));
    }

    /// <summary>
    /// Computes the full list of ability entries available to a player, along with
    /// each ability's effective mastery level for packet serialization.
    /// </summary>
    /// <param name="careerLine">Career line (1–24).</param>
    /// <param name="level">Player level.</param>
    /// <param name="mastery">Parsed mastery specialization state.</param>
    /// <returns>List of (entry, masteryLevel) pairs for all available abilities.</returns>
    public List<ResolvedAbility> Resolve(byte careerLine, byte level, MasteryState mastery)
    {
        ArgumentNullException.ThrowIfNull(mastery);

        var result = new List<ResolvedAbility>();

        // 1. Core abilities: MasteryTree == 0, available if MinimumRank <= level
        var coreAbilities = _abilityData.GetCoreAbilities(careerLine);
        foreach (var def in coreAbilities)
        {
            if (def.MinimumRank <= level)
            {
                // Core abilities use effective level as their mastery level
                result.Add(new ResolvedAbility(def.Entry, level));
            }
        }

        // 2. Mastery abilities: only if the player has activated the skill slot
        var masteryAbilities = _abilityData.GetMasteryAbilities(careerLine);
        foreach (var def in masteryAbilities)
        {
            if (def.MasteryTree is 0 or null or > MasteryState.TreeCount)
                continue;

            // V1 formula: slot index = (PointCost - 1) / 2 - 1
            var slotIndex = (def.PointCost - 1) / 2 - 1;
            if (slotIndex is < 0 or >= MasteryState.SlotsPerTree)
                continue;

            if (mastery.IsSkillActive(def.MasteryTree.Value - 1, slotIndex))
            {
                // Mastery abilities use the mastery level for their tree
                var masteryLevel = ComputeMasteryLevel(level, mastery, def.MasteryTree.Value);
                result.Add(new ResolvedAbility(def.Entry, masteryLevel));
            }
        }

        return result;
    }

    /// <summary>
    /// Returns all mastery ability definitions for a career line, with their tree/slot positions.
    /// Used to build the mastery tree packets during init.
    /// </summary>
    public List<MasteryAbilitySlot> GetMasterySlots(byte careerLine, MasteryState mastery)
    {
        ArgumentNullException.ThrowIfNull(mastery);

        var result = new List<MasteryAbilitySlot>();
        var masteryAbilities = _abilityData.GetMasteryAbilities(careerLine);

        foreach (var def in masteryAbilities)
        {
            if (def.MasteryTree is 0 or null or > MasteryState.TreeCount)
                continue;

            var slotIndex = (def.PointCost - 1) / 2 - 1;
            if (slotIndex is < 0 or >= MasteryState.SlotsPerTree)
                continue;

            var treeIndex = def.MasteryTree - 1; // 0-based
            var isActive = mastery.IsSkillActive(treeIndex.Value, slotIndex);
            result.Add(new MasteryAbilitySlot(def, (byte)treeIndex, (byte)slotIndex, isActive));
        }

        return result;
    }

    /// <summary>
    /// V1 mastery level formula:
    /// <c>10 + (level - 10) / 2 + pointsInTree</c>
    /// (simplified — no bolster for now).
    /// Returns the player's effective level if they're below level 11 or tree is 0.
    /// </summary>
    internal static byte ComputeMasteryLevel(byte level, MasteryState mastery, byte masteryTree)
    {
        if (masteryTree == 0 || level < 11)
            return level;

        var treePoints = mastery.GetTreePoints(masteryTree - 1);
        return (byte)(10 + (level - 10) / 2 + treePoints);
    }
}

/// <summary>
/// A resolved ability: entry ID + effective mastery level for packet encoding.
/// </summary>
/// <param name="Entry">Ability entry ID.</param>
/// <param name="MasteryLevel">Effective mastery level (player level for core, computed for spec).</param>
public readonly record struct ResolvedAbility(ushort Entry, byte MasteryLevel);

/// <summary>
/// A mastery ability in its tree/slot position, with activation state.
/// </summary>
/// <param name="Definition">The ability definition.</param>
/// <param name="TreeIndex">0-based tree index (0–2).</param>
/// <param name="SlotIndex">0-based slot index (0–6).</param>
/// <param name="IsActive">Whether this skill has been purchased.</param>
public readonly record struct MasteryAbilitySlot(
    AbilityDefinition Definition, byte TreeIndex, byte SlotIndex, bool IsActive);
