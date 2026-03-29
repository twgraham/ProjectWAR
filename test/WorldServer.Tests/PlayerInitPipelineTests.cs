using System.Collections.Frozen;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using WorldServerV2.Data;
using WorldServerV2.Data.Domain;
using WorldServerV2.Data.Entities;
using WorldServerV2.Data.Models;
using WorldServerV2.Network;
using WorldServerV2.Network.Dtos;
using WorldServerV2.Services;
using WorldServerV2.World.Combat.Abilities;
using WorldServerV2.World.Entities;
using WorldServerV2.World.Stats;

namespace WorldServer.Tests;

/// <summary>
/// Tests for Step 9: PlayerInitPipeline stat wiring — career base stat loading,
/// derived stat formulas, BuildStatsResponse, and full Initialize flow.
/// </summary>
public class PlayerInitPipelineTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Typical Ironbreaker career line ID.</summary>
    private const byte IronbreakerCareerLine = 1;

    /// <summary>Typical level-40 base stats for a melee career (Ironbreaker-like).</summary>
    private static readonly CareerStatEntry[] Level40MeleeStats =
    [
        new(StatId.Strength, 200),
        new(StatId.Agility, 100),
        new(StatId.Willpower, 120),
        new(StatId.Toughness, 250),
        new(StatId.Wounds, 400),
        new(StatId.Initiative, 110),
        new(StatId.WeaponSkill, 220),
        new(StatId.BallisticSkill, 50),
        new(StatId.Intelligence, 80),
        new(StatId.SpiritResistance, 60),
        new(StatId.ElementalResistance, 55),
        new(StatId.CorporealResistance, 65),
    ];

    /// <summary>Small level-10 stat set for basic testing.</summary>
    private static readonly CareerStatEntry[] Level10Stats =
    [
        new(StatId.Strength, 50),
        new(StatId.Wounds, 100),
        new(StatId.WeaponSkill, 55),
        new(StatId.Initiative, 30),
        new(StatId.Willpower, 40),
    ];

    private static IGameDataStore MakeStore(
        byte careerLine, byte level, CareerStatEntry[] stats)
    {
        var data = new CareerStatData(
            new[] { (careerLine, level, stats) }
                .ToFrozenDictionary(e => (e.careerLine, e.level), e => e.stats));
        return MakeStoreFromData(data);
    }

    private static IGameDataStore MakeMultiStore(
        params (byte CareerLine, byte Level, CareerStatEntry[] Stats)[] entries)
    {
        var data = new CareerStatData(
            entries.ToFrozenDictionary(e => (e.CareerLine, e.Level), e => e.Stats));
        return MakeStoreFromData(data);
    }

    private static IGameDataStore EmptyStore() =>
        MakeStoreFromData(CareerStatData.Empty);

    private static IGameDataStore MakeStoreFromData(CareerStatData data) =>
        MakeStoreFromData(data, AbilityData.Empty);

    private static IGameDataStore MakeStoreFromData(CareerStatData data, AbilityData abilityData)
    {
        var store = new GameDataStore();
        store.Initialize(new GameDataStore.Snapshot(
            new ClassData(
                FrozenDictionary<Class, ClassInfo>.Empty,
                FrozenDictionary<Class, List<ClassInfoItem>>.Empty),
            new ItemData(FrozenDictionary<uint, ItemInfo>.Empty),
            new CreatureData(
                FrozenDictionary<uint, CreatureProto>.Empty,
                FrozenDictionary<uint, CreatureSpawn>.Empty),
            new ZoneData(
                FrozenDictionary<ushort, ZoneInfo>.Empty,
                FrozenDictionary<uint, ZoneJump>.Empty),
            data,
            abilityData));
        return store;
    }

    private static PlayerEntity MakePlayer(
        byte careerLine = IronbreakerCareerLine,
        byte level = 40,
        byte realm = 1,
        ushort oid = 1)
    {
        var character = new Character
        {
            CharacterId = 100,
            Name = "TestPlayer",
            CareerLine = careerLine,
            Career = 20,
            Realm = realm,
            Level = level,
            Value = new CharacterValue
            {
                Level = level,
                Speed = 100,
                RegionId = 8,
                WorldX = 400_000,
                WorldY = 500_000,
                WorldZ = 1000,
                WorldO = 2048,
            },
            Items = [],
        };
        return new PlayerEntity(oid, character, 1); // maxHealth=1, will be overwritten by stats
    }

    private static PlayerInitPipeline MakePipeline(IGameDataStore store) =>
        new(NullLogger<PlayerInitPipeline>.Instance,
            new RealmInfo { RealmId = 1, Name = "TestRealm" },
            store);

    /// <summary>
    /// Reads a stat value from a <see cref="PlayerStatsResponse"/> at the given
    /// zero-based index. Returns (statId, value).
    /// </summary>
    private static (byte StatId, ushort Value) ReadStat(PlayerStatsResponse response, int index)
    {
        var offset = index * 3;
        var statId = response.StatEntries[offset];
        var value = (ushort)((response.StatEntries[offset + 1] << 8) | response.StatEntries[offset + 2]);
        return (statId, value);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  CareerStatData lookup
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CareerStatData_returns_stats_for_known_career_level()
    {
        var store = MakeStore(IronbreakerCareerLine, 40, Level40MeleeStats);
        var stats = store.CareerStats.GetBaseStats(IronbreakerCareerLine, 40);

        stats.Length.ShouldBe(Level40MeleeStats.Length);
        stats[0].Stat.ShouldBe(StatId.Strength);
        stats[0].Value.ShouldBe((ushort)200);
    }

    [Fact]
    public void CareerStatData_returns_empty_for_unknown_combination()
    {
        var store = MakeStore(IronbreakerCareerLine, 40, Level40MeleeStats);
        var stats = store.CareerStats.GetBaseStats(99, 40);

        stats.Length.ShouldBe(0);
    }

    [Fact]
    public void CareerStatData_supports_multiple_career_level_combinations()
    {
        var store = MakeMultiStore(
            (1, 40, Level40MeleeStats),
            (2, 10, Level10Stats));

        store.CareerStats.GetBaseStats(1, 40).Length.ShouldBe(Level40MeleeStats.Length);
        store.CareerStats.GetBaseStats(2, 10).Length.ShouldBe(Level10Stats.Length);
        store.CareerStats.GetBaseStats(1, 10).Length.ShouldBe(0); // Not registered
    }

    // ═══════════════════════════════════════════════════════════════════
    //  DerivedStatFormulas
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ParrySkill_at_level40_with_220WS_matches_V1_formula()
    {
        // Formula: WS / ((7.5 * 40 + 50) * 0.075) * 100
        // = 220 / ((350) * 0.075) * 100
        // = 220 / 26.25 * 100 = 838.095... → 838
        var result = DerivedStatFormulas.ParrySkill(220, 40);
        result.ShouldBe((ushort)838);
    }

    [Fact]
    public void EvadeSkill_at_level40_with_110Init_matches_V1_formula()
    {
        // Formula: 110 / ((7.5 * 40 + 50) * 0.075) * 100
        // = 110 / 26.25 * 100 = 419.047... → 419
        var result = DerivedStatFormulas.EvadeSkill(110, 40);
        result.ShouldBe((ushort)419);
    }

    [Fact]
    public void DisruptSkill_at_level40_with_120WP_matches_V1_formula()
    {
        // Formula: 120 / (26.25) * 100 = 457.14... → 457
        var result = DerivedStatFormulas.DisruptSkill(120, 40);
        result.ShouldBe((ushort)457);
    }

    [Fact]
    public void BlockSkill_at_level40_with_shield_matches_V1_formula()
    {
        // Formula: shieldArmor / ((7.5 * 40 + 50) * 0.2) * 100
        // = 500 / (350 * 0.2) * 100 = 500 / 70 * 100 = 714.28... → 714
        var result = DerivedStatFormulas.BlockSkill(500, 40);
        result.ShouldBe((ushort)714);
    }

    [Fact]
    public void BlockSkill_returns_zero_with_no_shield()
    {
        DerivedStatFormulas.BlockSkill(0, 40).ShouldBe((ushort)0);
    }

    [Fact]
    public void DerivedFormulas_return_zero_at_level_zero()
    {
        DerivedStatFormulas.ParrySkill(100, 0).ShouldBe((ushort)0);
        DerivedStatFormulas.EvadeSkill(100, 0).ShouldBe((ushort)0);
        DerivedStatFormulas.DisruptSkill(100, 0).ShouldBe((ushort)0);
        DerivedStatFormulas.BlockSkill(100, 0).ShouldBe((ushort)0);
    }

    [Fact]
    public void DerivedFormulas_return_zero_for_zero_stat()
    {
        DerivedStatFormulas.ParrySkill(0, 40).ShouldBe((ushort)0);
        DerivedStatFormulas.EvadeSkill(0, 40).ShouldBe((ushort)0);
        DerivedStatFormulas.DisruptSkill(0, 40).ShouldBe((ushort)0);
    }

    [Fact]
    public void DerivedFormulas_return_zero_for_negative_stat()
    {
        DerivedStatFormulas.ParrySkill(-100, 40).ShouldBe((ushort)0);
    }

    [Theory]
    [InlineData(5, 0)]   // level 5 → 0 slots
    [InlineData(10, 1)]  // level 10 → 1 slots (first unlock)
    [InlineData(11, 1)]  // level 11 → 1 slot
    [InlineData(19, 1)]  // level 19 → 1 slots
    [InlineData(20, 2)]  // level 20 → 2 slots
    [InlineData(30, 3)]  // level 31 → 3 slots
    [InlineData(40, 4)]  // level 40 → 4 slots
    public void TacticSlots_matches_expected(byte level, byte expected)
    {
        DerivedStatFormulas.TacticSlots(level).ShouldBe(expected);
    }

    [Fact]
    public void ParrySkill_at_level10_with_55WS()
    {
        // Formula: 55 / ((7.5 * 10 + 50) * 0.075) * 100
        // = 55 / ((125) * 0.075) * 100
        // = 55 / 9.375 * 100 = 586.66... → 586
        var result = DerivedStatFormulas.ParrySkill(55, 10);
        result.ShouldBe((ushort)586);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  BuildStatsResponse — stat wiring
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildStatsResponse_reads_primary_stats_from_StatContainer()
    {
        var player = MakePlayer(level: 40);
        player.Level = 40;
        player.Stats.SetBase(StatId.Strength, 200);
        player.Stats.SetBase(StatId.Agility, 100);
        player.Stats.SetBase(StatId.Wounds, 400);

        var response = PlayerInitPipeline.BuildStatsResponse(player);

        var (strId, strVal) = ReadStat(response, 0);
        strId.ShouldBe((byte)StatId.Strength);
        strVal.ShouldBe((ushort)200);

        var (agiId, agiVal) = ReadStat(response, 1);
        agiId.ShouldBe((byte)StatId.Agility);
        agiVal.ShouldBe((ushort)100);

        var (wndId, wndVal) = ReadStat(response, 4);
        wndId.ShouldBe((byte)StatId.Wounds);
        wndVal.ShouldBe((ushort)400);
    }

    [Fact]
    public void BuildStatsResponse_sets_level_and_bolster_level()
    {
        var player = MakePlayer(level: 25);
        player.Level = 25;

        var response = PlayerInitPipeline.BuildStatsResponse(player);

        response.Level.ShouldBe((byte)25);
        response.BolsterLevel.ShouldBe((byte)25);
    }

    [Fact]
    public void BuildStatsResponse_sets_tactic_slots_from_level()
    {
        var player = MakePlayer(level: 31);
        player.Level = 31;

        var response = PlayerInitPipeline.BuildStatsResponse(player);

        response.TacticSlots.ShouldBe((byte)3); // 31 / 10 = 3
    }

    [Fact]
    public void BuildStatsResponse_sets_armor_from_StatContainer()
    {
        var player = MakePlayer(level: 40);
        player.Level = 40;
        player.Stats.SetBase(StatId.Armor, 1500);

        var response = PlayerInitPipeline.BuildStatsResponse(player);

        response.Armor.ShouldBe((ushort)1500);
    }

    [Fact]
    public void BuildStatsResponse_computes_derived_parry_skill()
    {
        var player = MakePlayer(level: 40);
        player.Level = 40;
        player.Stats.SetBase(StatId.WeaponSkill, 220);

        var response = PlayerInitPipeline.BuildStatsResponse(player);

        var (statId, value) = ReadStat(response, 10); // index 10 = ParrySkill
        statId.ShouldBe((byte)StatId.ParrySkill);
        value.ShouldBe(DerivedStatFormulas.ParrySkill(220, 40));
    }

    [Fact]
    public void BuildStatsResponse_computes_derived_evade_skill()
    {
        var player = MakePlayer(level: 40);
        player.Level = 40;
        player.Stats.SetBase(StatId.Initiative, 110);

        var response = PlayerInitPipeline.BuildStatsResponse(player);

        var (statId, value) = ReadStat(response, 11); // index 11 = EvadeSkill
        statId.ShouldBe((byte)StatId.EvadeSkill);
        value.ShouldBe(DerivedStatFormulas.EvadeSkill(110, 40));
    }

    [Fact]
    public void BuildStatsResponse_computes_derived_disrupt_skill()
    {
        var player = MakePlayer(level: 40);
        player.Level = 40;
        player.Stats.SetBase(StatId.Willpower, 120);

        var response = PlayerInitPipeline.BuildStatsResponse(player);

        var (statId, value) = ReadStat(response, 12); // index 12 = DisruptSkill
        statId.ShouldBe((byte)StatId.DisruptSkill);
        value.ShouldBe(DerivedStatFormulas.DisruptSkill(120, 40));
    }

    [Fact]
    public void BuildStatsResponse_sets_block_skill_to_zero_without_shield()
    {
        var player = MakePlayer(level: 40);
        player.Level = 40;

        var response = PlayerInitPipeline.BuildStatsResponse(player);

        var (statId, value) = ReadStat(response, 9); // index 9 = BlockSkill
        statId.ShouldBe((byte)StatId.BlockSkill);
        value.ShouldBe((ushort)0);
    }

    [Fact]
    public void BuildStatsResponse_sets_resistances_from_StatContainer()
    {
        var player = MakePlayer(level: 40);
        player.Level = 40;
        player.Stats.SetBase(StatId.SpiritResistance, 60);
        player.Stats.SetBase(StatId.ElementalResistance, 55);
        player.Stats.SetBase(StatId.CorporealResistance, 65);

        var response = PlayerInitPipeline.BuildStatsResponse(player);

        var (spiritId, spiritVal) = ReadStat(response, 13);
        spiritId.ShouldBe((byte)StatId.SpiritResistance);
        spiritVal.ShouldBe((ushort)60);

        var (elemId, elemVal) = ReadStat(response, 14);
        elemId.ShouldBe((byte)StatId.ElementalResistance);
        elemVal.ShouldBe((ushort)55);

        var (corpId, corpVal) = ReadStat(response, 15);
        corpId.ShouldBe((byte)StatId.CorporealResistance);
        corpVal.ShouldBe((ushort)65);
    }

    [Fact]
    public void BuildStatsResponse_sets_reserved_stats_17_20_to_zero()
    {
        var player = MakePlayer(level: 40);
        player.Level = 40;

        var response = PlayerInitPipeline.BuildStatsResponse(player);

        for (int i = 16; i < 20; i++)
        {
            var (_, value) = ReadStat(response, i);
            value.ShouldBe((ushort)0, $"Stat at index {i} should be zero");
        }
    }

    [Fact]
    public void BuildStatsResponse_sets_stat21_to_hardcoded_one()
    {
        var player = MakePlayer(level: 40);
        player.Level = 40;

        var response = PlayerInitPipeline.BuildStatsResponse(player);

        var (statId, value) = ReadStat(response, 20); // index 20 = stat 21
        statId.ShouldBe((byte)21);
        value.ShouldBe((ushort)1);
    }

    [Fact]
    public void BuildStatsResponse_includes_buff_bonuses_in_totals()
    {
        var player = MakePlayer(level: 40);
        player.Level = 40;
        player.Stats.SetBase(StatId.Strength, 200);
        player.Stats.AddBonus(StatId.Strength, 50, BuffClass.Buff0);

        var response = PlayerInitPipeline.BuildStatsResponse(player);

        var (_, strVal) = ReadStat(response, 0);
        strVal.ShouldBe((ushort)250); // 200 base + 50 buff
    }

    [Fact]
    public void BuildStatsResponse_total_stat_entries_is_21()
    {
        var player = MakePlayer(level: 40);
        player.Level = 40;

        var response = PlayerInitPipeline.BuildStatsResponse(player);

        response.StatEntries.Length.ShouldBe(63); // 21 × 3 bytes
        response.BaseStatCount.ShouldBe((byte)0x15); // 21
    }

    [Fact]
    public void BuildStatsResponse_with_all_level40_melee_stats()
    {
        var player = MakePlayer(level: 40);
        player.Level = 40;
        foreach (var entry in Level40MeleeStats)
            player.Stats.SetBase(entry.Stat, entry.Value);

        var response = PlayerInitPipeline.BuildStatsResponse(player);

        // Verify all 9 primary stats
        ReadStat(response, 0).Value.ShouldBe((ushort)200); // Strength
        ReadStat(response, 1).Value.ShouldBe((ushort)100); // Agility
        ReadStat(response, 2).Value.ShouldBe((ushort)120); // Willpower
        ReadStat(response, 3).Value.ShouldBe((ushort)250); // Toughness
        ReadStat(response, 4).Value.ShouldBe((ushort)400); // Wounds
        ReadStat(response, 5).Value.ShouldBe((ushort)110); // Initiative
        ReadStat(response, 6).Value.ShouldBe((ushort)220); // WeaponSkill
        ReadStat(response, 7).Value.ShouldBe((ushort)50);  // BallisticSkill
        ReadStat(response, 8).Value.ShouldBe((ushort)80);  // Intelligence

        // Derived skills from primary stats
        ReadStat(response, 10).Value.ShouldBe(DerivedStatFormulas.ParrySkill(220, 40));
        ReadStat(response, 11).Value.ShouldBe(DerivedStatFormulas.EvadeSkill(110, 40));
        ReadStat(response, 12).Value.ShouldBe(DerivedStatFormulas.DisruptSkill(120, 40));

        // Resistances
        ReadStat(response, 13).Value.ShouldBe((ushort)60);  // Spirit
        ReadStat(response, 14).Value.ShouldBe((ushort)55);  // Elemental
        ReadStat(response, 15).Value.ShouldBe((ushort)65);  // Corporeal
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Initialize — full pipeline flow
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Initialize_loads_career_base_stats_into_StatContainer()
    {
        var store = MakeStore(IronbreakerCareerLine, 40, Level40MeleeStats);
        var pipeline = MakePipeline(store);
        var player = MakePlayer(careerLine: IronbreakerCareerLine, level: 40);
        var (session, _) = CreateSession();

        pipeline.Initialize(player, session);

        // Base stats should be loaded from provider into StatContainer
        player.Stats.GetTotal(StatId.Strength).ShouldBe(200);
        player.Stats.GetTotal(StatId.Wounds).ShouldBe(400);
        player.Stats.GetTotal(StatId.WeaponSkill).ShouldBe(220);
    }

    [Fact]
    public void Initialize_computes_max_health_from_wounds()
    {
        var store = MakeStore(IronbreakerCareerLine, 40, Level40MeleeStats);
        var pipeline = MakePipeline(store);
        var player = MakePlayer(careerLine: IronbreakerCareerLine, level: 40);
        var (session, _) = CreateSession();

        pipeline.Initialize(player, session);

        // MaxHealth = Wounds × 10 = 400 × 10 = 4000
        player.Health.Max.ShouldBe(4000u);
    }

    [Fact]
    public void Initialize_heals_player_to_full_after_stat_load()
    {
        var store = MakeStore(IronbreakerCareerLine, 40, Level40MeleeStats);
        var pipeline = MakePipeline(store);
        var player = MakePlayer(careerLine: IronbreakerCareerLine, level: 40);
        var (session, _) = CreateSession();

        pipeline.Initialize(player, session);

        player.Health.Current.ShouldBe(player.Health.Max);
    }

    [Fact]
    public void Initialize_sets_level_realm_faction()
    {
        var store = MakeStore(IronbreakerCareerLine, 40, Level40MeleeStats);
        var pipeline = MakePipeline(store);
        var player = MakePlayer(careerLine: IronbreakerCareerLine, level: 40, realm: 1);
        var (session, _) = CreateSession();

        pipeline.Initialize(player, session);

        player.Level.ShouldBe((byte)40);
        player.Realm.ShouldBe((byte)1);
        player.Faction.ShouldBe((byte)1);
    }

    [Fact]
    public void Initialize_sets_action_points_to_default_250()
    {
        var store = MakeStore(IronbreakerCareerLine, 40, Level40MeleeStats);
        var pipeline = MakePipeline(store);
        var player = MakePlayer(careerLine: IronbreakerCareerLine, level: 40);
        var (session, _) = CreateSession();

        pipeline.Initialize(player, session);

        player.ActionPoints.ShouldBe(250);
    }

    [Fact]
    public void Initialize_sends_expected_packet_count()
    {
        var store = MakeStore(IronbreakerCareerLine, 40, Level40MeleeStats);
        var pipeline = MakePipeline(store);
        var player = MakePlayer(careerLine: IronbreakerCareerLine, level: 40);
        var (session, stub) = CreateSession();

        pipeline.Initialize(player, session);

        // Pipeline sends: speed, initted, stats, skillList, abilityList, moraleList, tactics,
        // careerCategory, 3× masteryTreePoints, 3× careerPackageUpdate,
        // health, loaded, speed, stats = 18 packets
        // (No career ability packages because AbilityData is empty)
        stub.PacketCount.ShouldBe(18);
    }

    [Fact]
    public void Initialize_with_career_abilities_sends_career_packages()
    {
        // Build ability data with 2 melee abilities, 1 tactic, 1 morale
        var abilityData = BuildTestAbilityDataWithTactics();
        var store = MakeStoreFromData(
            new CareerStatData(
                new Dictionary<(byte, byte), CareerStatEntry[]>
                {
                    [(IronbreakerCareerLine, 40)] = Level40MeleeStats,
                }.ToFrozenDictionary()),
            abilityData);

        var pipeline = MakePipeline(store);
        var player = MakePlayer(careerLine: IronbreakerCareerLine, level: 40);
        var (session, stub) = CreateSession();

        pipeline.Initialize(player, session);

        // Base 18 packets + 3 F_CAREER_CATEGORY (abilities, tactics, morale) +
        // 2 CareerAbilityResponse (abilities) + 1 CareerAbilityResponse (tactic) +
        // 1 CareerAbilityResponse (morale) = 25
        stub.PacketCount.ShouldBe(25);

        // Verify career ability packets have correct fields
        var careerAbilities = stub.FindPackets<CareerAbilityResponse>();
        careerAbilities.Count.ShouldBe(4);

        // First ability: treeId=0, dbEntry=2756
        // PackageId = 2756 + 1399 (Dwarf offset) = 4155
        var first = careerAbilities.First(p => p.ReferenceId == 2756);
        first.TreeId.ShouldBe((byte)0); // abilities tree
        first.EntryIndex.ShouldBe((ushort)1);
        first.MinimumRank.ShouldBe(abilityData.CoreAbilitiesByCareer[IronbreakerCareerLine][0].MinimumRank); // (1+3)/2 = 2
        first.CashCost.ShouldBe(abilityData.CoreAbilitiesByCareer[IronbreakerCareerLine][0].CashCost);
        first.PackageId.ShouldBe(4155u); // 2756 + 1399 (Dwarf offset)
        first.ReferenceId.ShouldBe(2756u); // raw DB entry
        first.AbilityName.ShouldBe("Core Strike");

        // Tactic: treeId=1
        var tactic = careerAbilities.First(p => p.ReferenceId == 2791);
        tactic.TreeId.ShouldBe((byte)1); // tactics tree
        tactic.AbilityName.ShouldBe("Tactic A");

        // Morale: treeId=2
        var morale = careerAbilities.First(p => p.ReferenceId == 2780);
        morale.TreeId.ShouldBe((byte)2); // class morale tree
        morale.AbilityName.ShouldBe("Morale 1");
    }

    [Fact]
    public void Initialize_with_empty_provider_still_succeeds_with_zero_stats()
    {
        var pipeline = MakePipeline(EmptyStore());
        var player = MakePlayer(careerLine: 99, level: 40); // No stats registered for career 99
        var (session, _) = CreateSession();

        pipeline.Initialize(player, session);

        // All stats should be 0 (no base stats loaded)
        player.Stats.GetTotal(StatId.Strength).ShouldBe(0);
        // MaxHealth = max(1, 0 * 10) = 1 — floors to 1
        player.Health.Max.ShouldBe(1u);
    }

    [Fact]
    public void Initialize_with_level10_stats_computes_correct_health()
    {
        var store = MakeStore(2, 10, Level10Stats);
        var pipeline = MakePipeline(store);
        var player = MakePlayer(careerLine: 2, level: 10);
        var (session, _) = CreateSession();

        pipeline.Initialize(player, session);

        // Wounds = 100, MaxHealth = 100 × 10 = 1000
        player.Health.Max.ShouldBe(1000u);
        player.Health.Current.ShouldBe(1000u);
        player.Stats.GetTotal(StatId.Wounds).ShouldBe(100);
    }

    [Fact]
    public void Initialize_stat_dirty_flag_cleared_after_flush()
    {
        var store = MakeStore(IronbreakerCareerLine, 40, Level40MeleeStats);
        var pipeline = MakePipeline(store);
        var player = MakePlayer(careerLine: IronbreakerCareerLine, level: 40);
        var (session, _) = CreateSession();

        pipeline.Initialize(player, session);

        // After Initialize, Flush was called so dirty flag should be false
        player.Stats.IsDirty.ShouldBeFalse();
    }

    [Fact]
    public void Initialize_health_packet_reflects_stat_derived_max_health()
    {
        var store = MakeStore(IronbreakerCareerLine, 40, Level40MeleeStats);
        var pipeline = MakePipeline(store);
        var player = MakePlayer(careerLine: IronbreakerCareerLine, level: 40);
        var (session, stub) = CreateSession();

        pipeline.Initialize(player, session);

        // The health packet (4th packet) should have stat-derived maxHealth
        var healthPacket = stub.FindPacket<PlayerHealthResponse>();
        healthPacket.ShouldNotBeNull();
        healthPacket.MaxHealth.ShouldBe(4000u); // Wounds(400) × 10
        healthPacket.Health.ShouldBe(4000u);     // Full after Heal
        healthPacket.MaxActionPoints.ShouldBe((ushort)250);
    }

    [Fact]
    public void Initialize_throws_on_null_player()
    {
        var pipeline = MakePipeline(EmptyStore());
        var (session, _) = CreateSession();

        Should.Throw<ArgumentNullException>(() => pipeline.Initialize(null!, session));
    }

    [Fact]
    public void Initialize_throws_on_null_session()
    {
        var pipeline = MakePipeline(EmptyStore());
        var player = MakePlayer();

        Should.Throw<ArgumentNullException>(() => pipeline.Initialize(player, null!));
    }

    // ═════════════════════════════════════════════════════════════════
    //  Career ability data builder
    // ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds test ability data with abilities, tactics, and morales for career line 1.
    /// Category values match the DB's career tree assignment for grouping.
    /// </summary>
    private static AbilityData BuildTestAbilityDataWithTactics()
    {
        const uint career1Bitmask = 1u;

        // Use realistic Ironbreaker-range entry IDs (>1399 so referenceId stays positive)
        var abilities = new AbilityDefinition[]
        {
            new()
            {
                Entry = 2756, Name = "Core Strike", CareerLine = career1Bitmask,
                MasteryTree = 0, MinimumRank = 1, AbilityType = AbilityType.Melee,
                Category = 0, // Class Abilities tree
            },
            new()
            {
                Entry = 2757, Name = "Core Slash", CareerLine = career1Bitmask,
                MasteryTree = 0, MinimumRank = 10, AbilityType = AbilityType.Melee,
                Category = 0, // Class Abilities tree
            },
            new()
            {
                Entry = 2791, Name = "Tactic A", CareerLine = career1Bitmask,
                MasteryTree = 0, MinimumRank = 5, AbilityType = AbilityType.Effect,
                Category = 1, // Class Tactics tree
            },
            new()
            {
                Entry = 2780, Name = "Morale 1", CareerLine = career1Bitmask,
                MasteryTree = 0, MinimumRank = 8, Origin = AbilityOrigin.Morale,
                Category = 2, // Class Morale tree
            },
        };

        var byEntry = abilities.ToFrozenDictionary(a => a.Entry);
        var coreByCareer = new Dictionary<byte, AbilityDefinition[]>
        {
            [1] = abilities.Where(a => a.MasteryTree == 0).OrderBy(a => a.MinimumRank).ToArray(),
        }.ToFrozenDictionary();

        return new AbilityData(
            byEntry,
            coreByCareer,
            FrozenDictionary<byte, AbilityDefinition[]>.Empty);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Stub session for testing packet sends
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a <see cref="GameSession"/> backed by a fake connection context
    /// that records sent packets.
    /// </summary>
    private static (GameSession Session, StubConnectionContext Stub) CreateSession()
    {
        var stub = new StubConnectionContext();
        var session = new GameSession(1, stub);
        session.State = ClientState.CharScreen;
        return (session, stub);
    }

    /// <summary>
    /// Fake <see cref="IConnectionContext"/> that records sent packets for assertion.
    /// </summary>
    private sealed class StubConnectionContext : Core.Infrastructure.Network.IConnectionContext
    {
        private readonly List<(byte Opcode, object Packet)> _sent = [];

        public int PacketCount => _sent.Count;

        public T? FindPacket<T>() where T : class =>
            _sent.FirstOrDefault(p => p.Packet is T).Packet as T;

        public List<T> FindPackets<T>() where T : class =>
            _sent.Where(p => p.Packet is T).Select(p => (T)p.Packet).ToList();

        public string? RemoteAddress => "127.0.0.1:12345";

        public Core.Infrastructure.Network.IPacketFramer PacketFramer =>
            throw new NotImplementedException("Not needed for unit tests");

        public void SendResponse<T>(byte opcode, T response)
        {
            _sent.Add((opcode, response!));
        }

        public void Disconnect(string reason, bool flush = false) { }

        public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();

        public void OnDispatchError(byte opcode, Exception exception) { }
    }
}
