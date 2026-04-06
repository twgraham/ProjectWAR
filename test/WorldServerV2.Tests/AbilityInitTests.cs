using System.Collections.Frozen;
using Core.Domain.Entities;
using Core.GameWorld.Abilities;
using Core.GameWorld.Combat.Abilities;
using Core.GameWorld.DataStore.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using WorldServerV2.Data;
using WorldServerV2.Data.Models;
using WorldServerV2.Network;
using WorldServerV2.Network.Dtos;
using WorldServerV2.Services;

namespace WorldServerV2.Tests;

/// <summary>
/// Tests for ability initialization: MasteryState/RenownState parsing,
/// AbilityResolver, and PlayerInitPipeline ability/career packet integration.
/// </summary>
public class AbilityInitTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  MasteryState
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void MasteryState_Parse_EmptyString_ReturnsEmpty()
    {
        MasteryState.Parse("").ShouldBeSameAs(MasteryState.Empty);
        MasteryState.Parse(null).ShouldBeSameAs(MasteryState.Empty);
        MasteryState.Parse("x").ShouldBeSameAs(MasteryState.Empty); // length < 2
    }

    [Fact]
    public void MasteryState_Parse_ValidString_ReturnsCorrectState()
    {
        var state = MasteryState.Parse("3;5;2;0,1,0,0,0,0,0;1,0,1,0,0,0,0;0,0,0,0,0,0,0");

        state.GetTreePoints(0).ShouldBe((byte)3);
        state.GetTreePoints(1).ShouldBe((byte)5);
        state.GetTreePoints(2).ShouldBe((byte)2);

        state.IsSkillActive(0, 1).ShouldBeTrue();
        state.IsSkillActive(0, 0).ShouldBeFalse();
        state.IsSkillActive(1, 0).ShouldBeTrue();
        state.IsSkillActive(1, 2).ShouldBeTrue();
        state.IsSkillActive(2, 0).ShouldBeFalse();
    }

    [Fact]
    public void MasteryState_TotalPointsSpent_SumsAllTrees()
    {
        var state = MasteryState.Parse("3;5;2;0,0,0,0,0,0,0;0,0,0,0,0,0,0;0,0,0,0,0,0,0");
        state.TotalPointsSpent.ShouldBe(10);
    }

    [Fact]
    public void MasteryState_Parse_MalformedString_ReturnsEmpty()
    {
        // Too few segments
        MasteryState.Parse("3;5").ShouldBeSameAs(MasteryState.Empty);
        // Invalid byte in points
        MasteryState.Parse("abc;5;2;0,0,0,0,0,0,0;0,0,0,0,0,0,0;0,0,0,0,0,0,0")
            .ShouldBeSameAs(MasteryState.Empty);
        // Too few skill slots
        MasteryState.Parse("3;5;2;0,1,0;0,0,0,0,0,0,0;0,0,0,0,0,0,0")
            .ShouldBeSameAs(MasteryState.Empty);
    }

    [Fact]
    public void MasteryState_Serialize_RoundTrips()
    {
        var original = "3;5;2;0,1,0,0,0,0,0;1,0,1,0,0,0,0;0,0,0,0,0,0,0";
        var state = MasteryState.Parse(original);
        state.Serialize().ShouldBe(original);
    }

    [Fact]
    public void MasteryState_Empty_Serializes_ToZeros()
    {
        MasteryState.Empty.Serialize().ShouldBe("0;0;0;0,0,0,0,0,0,0;0,0,0,0,0,0,0;0,0,0,0,0,0,0");
    }

    [Fact]
    public void MasteryState_GetTreePoints_OutOfRange_Throws()
    {
        var state = MasteryState.Empty;
        Should.Throw<ArgumentOutOfRangeException>(() => state.GetTreePoints(-1));
        Should.Throw<ArgumentOutOfRangeException>(() => state.GetTreePoints(3));
    }

    [Fact]
    public void MasteryState_IsSkillActive_OutOfRange_Throws()
    {
        var state = MasteryState.Empty;
        Should.Throw<ArgumentOutOfRangeException>(() => state.IsSkillActive(-1, 0));
        Should.Throw<ArgumentOutOfRangeException>(() => state.IsSkillActive(0, 7));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RenownState
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RenownState_Parse_EmptyString_ReturnsEmpty()
    {
        RenownState.Parse("").ShouldBeSameAs(RenownState.Empty);
        RenownState.Parse(null).ShouldBeSameAs(RenownState.Empty);
    }

    [Fact]
    public void RenownState_Parse_ValidString_ReturnsCorrectState()
    {
        var state = RenownState.Parse("0:0;0:1;2:3;4:0;");

        state.SlotCount.ShouldBe(4);
        state.IsTrained(0, 0).ShouldBeTrue();
        state.IsTrained(0, 1).ShouldBeTrue();
        state.IsTrained(2, 3).ShouldBeTrue();
        state.IsTrained(4, 0).ShouldBeTrue();
        state.IsTrained(1, 0).ShouldBeFalse();
    }

    [Fact]
    public void RenownState_Parse_SkipsMalformedEntries()
    {
        // "bad" entry is skipped, valid ones parsed
        var state = RenownState.Parse("0:0;bad;1:5;");
        state.SlotCount.ShouldBe(2);
        state.IsTrained(0, 0).ShouldBeTrue();
        state.IsTrained(1, 5).ShouldBeTrue();
    }

    [Fact]
    public void RenownState_Parse_OutOfBoundsEntries_Skipped()
    {
        // tree 7 and pos 20 are out of bounds
        var state = RenownState.Parse("7:0;0:20;0:5;");
        state.SlotCount.ShouldBe(1);
        state.IsTrained(0, 5).ShouldBeTrue();
    }

    [Fact]
    public void RenownState_Serialize_RoundTrips()
    {
        var state = RenownState.Parse("2:3;0:1;0:0;4:0;");
        var serialized = state.Serialize();
        // Should be sorted by tree then position, with trailing semicolon
        serialized.ShouldBe("0:0;0:1;2:3;4:0;");

        // Round-trip
        var reparsed = RenownState.Parse(serialized);
        reparsed.SlotCount.ShouldBe(4);
    }

    [Fact]
    public void RenownState_Empty_Serializes_ToEmptyString()
    {
        RenownState.Empty.Serialize().ShouldBe(string.Empty);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AbilityData
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AbilityData_Empty_ReturnsNoAbilities()
    {
        var data = AbilityData.Empty;
        data.GetCoreAbilities(1).Length.ShouldBe(0);
        data.GetMasteryAbilities(1).Length.ShouldBe(0);
        data.GetByEntry(100).ShouldBeNull();
    }

    [Fact]
    public void AbilityData_GetCoreAbilities_FiltersCorrectly()
    {
        var data = BuildTestAbilityData();
        var core = data.GetCoreAbilities(1);
        // Should include abilities that match career line 1 bitmask with MasteryTree == 0
        core.Length.ShouldBeGreaterThan(0);
        foreach (var def in core)
            def.MasteryTree.ShouldBe((byte)0);
    }

    [Fact]
    public void AbilityData_GetMasteryAbilities_FiltersCorrectly()
    {
        var data = BuildTestAbilityData();
        var mastery = data.GetMasteryAbilities(1);
        mastery.Length.ShouldBeGreaterThan(0);
        foreach (var def in mastery)
            def.MasteryTree!.Value.ShouldBeGreaterThan((byte)0);
    }

    [Fact]
    public void AbilityData_GetByEntry_FindsAbility()
    {
        var data = BuildTestAbilityData();
        var def = data.GetByEntry(1001);
        def.ShouldNotBeNull();
        def.Name.ShouldBe("Core Strike");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  AbilityResolver
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void AbilityResolver_Resolve_CoreAbilitiesFiltered_ByLevel()
    {
        var data = BuildTestAbilityData();
        var resolver = new AbilityResolver(data);

        // Level 5: should only include abilities with MinimumRank <= 5
        var abilities = resolver.Resolve(1, 5, MasteryState.Empty);
        abilities.ShouldAllBe(a => a.Entry == 1001); // only the rank-1 core ability
    }

    [Fact]
    public void AbilityResolver_Resolve_CoreAbilitiesFiltered_AllAtMaxLevel()
    {
        var data = BuildTestAbilityData();
        var resolver = new AbilityResolver(data);

        var abilities = resolver.Resolve(1, 40, MasteryState.Empty);
        // Should include both core abilities (rank 1 and rank 10)
        abilities.Count.ShouldBeGreaterThanOrEqualTo(2);
        abilities.ShouldContain(a => a.Entry == 1001);
        abilities.ShouldContain(a => a.Entry == 1002);
    }

    [Fact]
    public void AbilityResolver_Resolve_MasteryAbilities_OnlyIfActivated()
    {
        var data = BuildTestAbilityData();
        var resolver = new AbilityResolver(data);

        // Mastery ability 2001 is tree 1, slot 0 (PointCost=1 → slot = (1-1)/2-1 = -1... need PointCost=3)
        // Actually from V1: slot = (PointCost - 1) / 2 - 1
        // PointCost=3 → (3-1)/2 - 1 = 0 (slot 0)
        // Our test data has mastery ability 2001 at tree 1, PointCost 3 → slot 0

        // No mastery skills active
        var withoutMastery = resolver.Resolve(1, 40, MasteryState.Empty);
        withoutMastery.ShouldNotContain(a => a.Entry == 2001);

        // Activate tree 0, slot 0 (tree index is 0-based for MasteryState)
        var mastery = MasteryState.Parse("5;0;0;1,0,0,0,0,0,0;0,0,0,0,0,0,0;0,0,0,0,0,0,0");
        var withMastery = resolver.Resolve(1, 40, mastery);
        withMastery.ShouldContain(a => a.Entry == 2001);
    }

    [Fact]
    public void AbilityResolver_Resolve_MasteryLevel_UsesPlayerLevelForCore()
    {
        var data = BuildTestAbilityData();
        var resolver = new AbilityResolver(data);

        var abilities = resolver.Resolve(1, 35, MasteryState.Empty);
        var coreAbility = abilities.First(a => a.Entry == 1001);
        coreAbility.MasteryLevel.ShouldBe((byte)35);
    }

    [Fact]
    public void AbilityResolver_ComputeMasteryLevel_BelowLevel11_ReturnsLevel()
    {
        AbilityResolver.ComputeMasteryLevel(10, MasteryState.Empty, 1).ShouldBe((byte)10);
        AbilityResolver.ComputeMasteryLevel(5, MasteryState.Empty, 1).ShouldBe((byte)5);
    }

    [Fact]
    public void AbilityResolver_ComputeMasteryLevel_AtLevel40With5Points()
    {
        var mastery = MasteryState.Parse("5;0;0;0,0,0,0,0,0,0;0,0,0,0,0,0,0;0,0,0,0,0,0,0");
        // 10 + (40 - 10) / 2 + 5 = 10 + 15 + 5 = 30
        AbilityResolver.ComputeMasteryLevel(40, mastery, 1).ShouldBe((byte)30);
    }

    [Fact]
    public void AbilityResolver_GetMasterySlots_ReturnsCorrectPositions()
    {
        var data = BuildTestAbilityData();
        var resolver = new AbilityResolver(data);
        var mastery = MasteryState.Parse("5;0;0;1,0,0,0,0,0,0;0,0,0,0,0,0,0;0,0,0,0,0,0,0");

        var slots = resolver.GetMasterySlots(1, mastery);
        slots.Count.ShouldBeGreaterThan(0);

        var slot0 = slots.First(s => s.Definition.Entry == 2001);
        slot0.TreeIndex.ShouldBe((byte)0);
        slot0.SlotIndex.ShouldBe((byte)0);
        slot0.IsActive.ShouldBeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PlayerInitPipeline — ability-related helpers
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildTacticsList_ReturnsNonZeroTactics()
    {
        var charValue = new CharacterValue
        {
            Level = 40,
            Tactic1 = 1000,
            Tactic2 = null,
            Tactic3 = 2000,
            Tactic4 = 0,
        };
        var tactics = PlayerInitPipeline.BuildTacticsList(charValue);
        tactics.ShouldBe([1000, 2000]);
    }

    [Fact]
    public void BuildTacticsList_EmptyWhenNoTactics()
    {
        var charValue = new CharacterValue { Level = 40 };
        var tactics = PlayerInitPipeline.BuildTacticsList(charValue);
        tactics.ShouldBeEmpty();
    }

    [Fact]
    public void ComputeTotalMasteryPoints_ZeroBelowLevel10()
    {
        PlayerInitPipeline.ComputeTotalMasteryPoints(1).ShouldBe(0);
        PlayerInitPipeline.ComputeTotalMasteryPoints(10).ShouldBe(0);
    }

    [Fact]
    public void ComputeTotalMasteryPoints_OnePerLevelAbove10()
    {
        PlayerInitPipeline.ComputeTotalMasteryPoints(11).ShouldBe(1);
        PlayerInitPipeline.ComputeTotalMasteryPoints(20).ShouldBe(10);
        PlayerInitPipeline.ComputeTotalMasteryPoints(40).ShouldBe(30);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CareerInfo — package ID offsets
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(1, 1399)]  // Ironbreaker (Dwarf)
    [InlineData(2, 1399)]  // Slayer (Dwarf)
    [InlineData(3, 1399)]  // Rune Priest (Dwarf)
    [InlineData(4, 1399)]  // Engineer (Dwarf)
    [InlineData(5, 1399)]  // Black Orc (Greenskin)
    [InlineData(6, 1399)]  // Choppa (Greenskin)
    [InlineData(7, 1399)]  // Shaman (Greenskin)
    [InlineData(8, 1399)]  // Squig Herder (Greenskin)
    public void CareerInfo_PackageIdOffset_DwarfGreenskin(byte careerLine, int expectedOffset)
    {
        CareerInfo.GetPackageIdOffset(careerLine).ShouldBe(expectedOffset);
    }

    [Theory]
    [InlineData(9, 4251)]   // Witch Hunter (Empire)
    [InlineData(10, 4251)]  // Knight of the BS (Empire)
    [InlineData(11, 4251)]  // Bright Wizard (Empire)
    [InlineData(12, 4251)]  // Warrior Priest (Empire)
    [InlineData(13, 4251)]  // Chosen (Chaos)
    [InlineData(14, 4251)]  // Marauder (Chaos)
    [InlineData(15, 4251)]  // Zealot (Chaos)
    [InlineData(16, 4251)]  // Magus (Chaos)
    public void CareerInfo_PackageIdOffset_EmpireChaos(byte careerLine, int expectedOffset)
    {
        CareerInfo.GetPackageIdOffset(careerLine).ShouldBe(expectedOffset);
    }

    [Theory]
    [InlineData(17, 4351)] // Swordmaster (High Elf)
    [InlineData(18, 4351)] // Shadow Warrior (High Elf)
    [InlineData(19, 4351)] // White Lion (High Elf)
    [InlineData(20, 4351)] // Archmage (High Elf)
    [InlineData(21, 4351)] // Black Guard (Dark Elf)
    [InlineData(22, 4351)] // Witch Elf (Dark Elf)
    [InlineData(23, 4351)] // Disciple (Dark Elf)
    [InlineData(24, 4351)] // Sorcerer (Dark Elf)
    public void CareerInfo_PackageIdOffset_Elves(byte careerLine, int expectedOffset)
    {
        CareerInfo.GetPackageIdOffset(careerLine).ShouldBe(expectedOffset);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(25)]
    [InlineData(255)]
    public void CareerInfo_PackageIdOffset_Unknown_ReturnsZero(byte careerLine)
    {
        CareerInfo.GetPackageIdOffset(careerLine).ShouldBe(0);
    }

    [Fact]
    public void CareerInfo_ComputePackageId_Ironbreaker_MatchesSniff()
    {
        // Sniff: Ironbreaker "Vengeful Strike" dbEntry=1357, packageId=2756
        CareerInfo.ComputePackageId(1357, 1).ShouldBe(2756u);
    }

    [Fact]
    public void CareerInfo_ComputePackageId_KotBS_MatchesSniff()
    {
        // Sniff: Knight of the BS "Press The Attack!" dbEntry=3753, packageId=8004
        CareerInfo.ComputePackageId(3753, 10).ShouldBe(8004u);
    }

    [Fact]
    public void CareerInfo_ComputePackageId_Archmage_MatchesSniff()
    {
        // Sniff: Archmage dbEntry=4888, packageId=9239
        CareerInfo.ComputePackageId(4888, 20).ShouldBe(9239u);
    }

    [Theory]
    [InlineData(1, "Ironbreaker")]
    [InlineData(10, "Knight of the BS")]
    [InlineData(20, "Archmage")]
    [InlineData(24, "Sorcerer")]
    public void CareerInfo_GetCareerName_ReturnsExpectedName(byte careerLine, string expected)
    {
        CareerInfo.GetCareerName(careerLine).ShouldBe(expected);
    }

    [Fact]
    public void CareerInfo_GetCareerName_Unknown_ReturnsFallback()
    {
        CareerInfo.GetCareerName(0).ShouldBe("");
        CareerInfo.GetCareerName(25).ShouldStartWith("Career");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a test AbilityData with a few abilities for career line 1:
    /// - 1001: Core Strike (core, rank 1)
    /// - 1002: Core Slash (core, rank 10)
    /// - 2001: Mastery Hit (mastery tree 1, PointCost 3 → slot 0)
    /// - 2002: Mastery Bash (mastery tree 2, PointCost 5 → slot 1)
    /// </summary>
    private static AbilityData BuildTestAbilityData()
    {
        // Career line 1 bitmask: bit 0 set = 1u
        const uint career1Bitmask = 1u;

        var abilities = new AbilityDefinition[]
        {
            new()
            {
                Entry = 1001, Name = "Core Strike", CareerLine = career1Bitmask,
                MasteryTree = 0, MinimumRank = 1, PointCost = 0,
            },
            new()
            {
                Entry = 1002, Name = "Core Slash", CareerLine = career1Bitmask,
                MasteryTree = 0, MinimumRank = 10, PointCost = 0,
            },
            new()
            {
                Entry = 2001, Name = "Mastery Hit", CareerLine = career1Bitmask,
                MasteryTree = 1, MinimumRank = 0, PointCost = 3, // slot = (3-1)/2 - 1 = 0
            },
            new()
            {
                Entry = 2002, Name = "Mastery Bash", CareerLine = career1Bitmask,
                MasteryTree = 2, MinimumRank = 0, PointCost = 5, // slot = (5-1)/2 - 1 = 1
            },
        };

        var byEntry = abilities.ToFrozenDictionary(a => a.Entry);
        var coreByCareer = new Dictionary<byte, AbilityDefinition[]>
        {
            [1] = abilities.Where(a => a.MasteryTree == 0).OrderBy(a => a.MinimumRank).ToArray(),
        }.ToFrozenDictionary();

        var masteryByCareer = new Dictionary<byte, AbilityDefinition[]>
        {
            [1] = abilities.Where(a => a.MasteryTree > 0).OrderBy(a => a.MasteryTree).ThenBy(a => a.PointCost).ToArray(),
        }.ToFrozenDictionary();

        return new AbilityData(byEntry, coreByCareer, masteryByCareer);
    }
}
