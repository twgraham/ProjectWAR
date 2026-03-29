using Microsoft.Extensions.Logging;
using WorldServerV2.Data;
using WorldServerV2.Data.Domain;
using WorldServerV2.Network;
using WorldServerV2.Network.Dtos;
using WorldServerV2.World.Abilities;
using WorldServerV2.World.Combat.Abilities;
using WorldServerV2.World.Entities;
using WorldServerV2.World.Stats;

namespace WorldServerV2.Services;

/// <summary>
/// Orchestrates the player initialization sequence that transitions a client from the
/// character screen into the live game world. Implements Phase B (compute) and Phase C
/// (serialize/send) of the three-phase model described in the architecture doc §10.
/// <para>
/// <b>Threading</b>: <see cref="Initialize"/> is invoked on the <b>handler thread</b>
/// after the caller has reserved an OID via <c>Region.ReserveOid()</c>. This keeps
/// all computation and packet serialization off the region tick loop.
/// <see cref="GameSession.Send{T}"/> is thread-safe (channel-based send queue), so
/// packets can be enqueued from any thread.
/// </para>
/// <para>
/// <b>Packet sequence</b> (minimum viable set):
/// <list type="number">
///   <item><c>F_MAX_VELOCITY</c> (0x1E) — movement speed</item>
///   <item><c>S_PLAYER_INITTED</c> (0x88) — identity, position, realm, career</item>
///   <item><c>F_CHARACTER_INFO</c> (0xBE) subtype 3 — skills, career, race, rally point</item>
///   <item><c>F_CHARACTER_INFO</c> (0xBE) subtype 1 — ability list</item>
///   <item><c>F_MORALE_LIST</c> (0x8C) — morale ability slots</item>
///   <item><c>F_TACTICS</c> (0xF7) — active tactic abilities</item>
///   <item><c>F_CAREER_CATEGORY</c> (0xEE) + <c>F_CAREER_PACKAGE_INFO</c> (0xF3) — career ability packages (trees 0–8, 16–19 by DB category)</item>
///   <item><c>F_CAREER_CATEGORY</c> (0xEE) — mastery tree header</item>
///   <item><c>F_CAREER_PACKAGE_INFO</c> (0xF3) — mastery tree points + skill entries</item>
///   <item><c>F_CAREER_PACKAGE_UPDATE</c> (0xF1) — mastery point summaries</item>
///   <item><c>F_PLAYER_STATS</c> (0x46) — base stats</item>
///   <item><c>F_PLAYER_HEALTH</c> (0x05) — HP, AP, morale</item>
///   <item><c>S_PLAYER_LOADED</c> (0x89) — data-complete marker</item>
///   <item><c>F_MAX_VELOCITY</c> (0x1E) — speed (sent twice, matching old server)</item>
///   <item><c>F_PLAYER_STATS</c> (0x46) — stats (sent twice, matching old server)</item>
/// </list>
/// <c>F_PLAYER_INIT_COMPLETE</c> (0xEF) is sent by the caller (<c>CharacterScreenHandler</c>)
/// after the entity has been placed in the region via <c>Region.AddAsync()</c>.
/// After the client processes init-complete, it sends <c>F_REQUEST_WORLD_LARGE</c>,
/// which is handled separately (sends <c>F_SET_TIME</c> + <c>S_WORLD_SENT</c>).
/// </para>
/// Registered as a <b>singleton</b> in the DI container.
/// </summary>
public sealed class PlayerInitPipeline
{
    /// <summary>Default action points for a new player.</summary>
    private const ushort DefaultActionPoints = 250;

    /// <summary>Default max action points.</summary>
    private const ushort DefaultMaxActionPoints = 250;

    private readonly ILogger<PlayerInitPipeline> _logger;
    private readonly RealmInfo _realmInfo;
    private readonly IGameDataStore _gameDataStore;
    private readonly AbilityResolver _abilityResolver;

    public PlayerInitPipeline(
        ILogger<PlayerInitPipeline> logger,
        RealmInfo realmInfo,
        IGameDataStore gameDataStore)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _realmInfo = realmInfo ?? throw new ArgumentNullException(nameof(realmInfo));
        _gameDataStore = gameDataStore ?? throw new ArgumentNullException(nameof(gameDataStore));
        _abilityResolver = new AbilityResolver(gameDataStore.Abilities);
    }

    /// <summary>
    /// Executes Phase B (compute mutable state) and Phase C (serialize and send packets)
    /// for the given player. Called on the handler thread after the entity has been
    /// assigned a pre-reserved OID via <c>Region.ReserveOid()</c>.
    /// </summary>
    /// <param name="player">The player entity with a pre-assigned OID.</param>
    /// <param name="session">The player's network session for sending packets.</param>
    public void Initialize(PlayerEntity player, GameSession session)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(session);

        var character = player.Character;
        var charValue = character.Value;

        // ── Phase B: Compute mutable entity state from DB record ────────

        player.Level = charValue.Level;
        player.Realm = character.Realm;
        // Faction mirrors realm for players (1 = Order, 2 = Destruction).
        player.Faction = character.Realm;

        // Load career base stats into StatContainer from the game data store.
        var baseStats = _gameDataStore.CareerStats.GetBaseStats(character.CareerLine, charValue.Level);
        foreach (var entry in baseStats)
            player.Stats.SetBase(entry.Stat, entry.Value);

        // Flush computes derived stats (MaxHealth = Wounds × 10) and fires
        // OnMaxHealthChanged → HealthComponent.Max is updated.
        player.Stats.Flush();

        // Health: top off to full after stat-driven max has been applied.
        // Use Heal (not Resurrect) because the entity is already alive and its
        // current HP was initialized to the pre-flush max; Flush() may raise Max.
        player.Health.Heal(player.Health.Max);

        var speed = charValue.Speed > 0 ? (ushort)charValue.Speed : (ushort)100;

        _logger.LogDebug(
            "Phase B complete for {Name} (OID {Oid}): Level={Level}, Realm={Realm}, " +
            "HP={Hp}/{MaxHp}, Speed={Speed}",
            player.Name, player.ObjectId, player.Level, player.Realm,
            player.Health.Current, player.Health.Max, speed);

        // ── Phase C: Serialize and send packets ─────────────────────────

        var speedPacket = new SpeedResponse
        {
            Speed = speed,
            CanMove = (byte)(speed > 0 ? 1 : 0),
            SpeedPercent = 100,
        };

        // 1. F_MAX_VELOCITY — speed first (matches old server order)
        session.SendSpeed(speedPacket);

        // 2. S_PLAYER_INITTED — identity, position, realm, career
        session.SendPlayerInitted(new PlayerInittedResponse
        {
            Oid = player.ObjectId,
            CharacterId = character.CharacterId,
            WorldZ = (ushort)charValue.WorldZ,
            WorldX = (uint)charValue.WorldX,
            WorldY = (uint)charValue.WorldY,
            WorldO = (ushort)charValue.WorldO,
            Realm = character.Realm,
            RegionId = (ushort)charValue.RegionId,
            Career = character.Career,
            RealmName = _realmInfo.Name,
        });

         // 3. Ability / career packets — requires mastery state
        var mastery = MasteryState.Parse(charValue.MasterySkills);

        // 3a. F_TACTICS — active tactic abilities
        var tactics = BuildTacticsList(charValue);
        session.SendTactics(new TacticsResponse { Tactics = tactics });

        // 3b. Career ability/tactic packages (F_CAREER_CATEGORY + F_CAREER_PACKAGE_INFO per category tree)
        SendCareerPackages(session, character.CareerLine);

        // 3c. Mastery tree packets (F_CAREER_CATEGORY → F_CAREER_PACKAGE_INFO → F_CAREER_PACKAGE_UPDATE)
        SendMasteryTree(session, character.CareerLine, charValue.Level, mastery);

        // 4. F_PLAYER_STATS — base stats (minimal: all zeros except level)
        var statsResponse = BuildStatsResponse(player);
        session.SendPlayerStats(statsResponse);

        // 5. F_CHARACTER_INFO subtype 3 — skills, career, race, rally point
        session.SendSkillList(new SkillListResponse
        {
            CareerLine = character.CareerLine,
            Race = character.Race,
            Skills = charValue.Skills,
            RallyPoint = charValue.RallyPoint,
        });

        // 5a. F_CHARACTER_INFO subtype 1 — ability list with mastery levels
        var resolvedAbilities = _abilityResolver.Resolve(character.CareerLine, charValue.Level, mastery);
        SendAbilityList(session, resolvedAbilities);

        // 5b. F_MORALE_LIST — 4 morale slots
        session.SendMoraleList(new MoraleListResponse
        {
            Slot1 = charValue.Morale1 ?? 0,
            Slot2 = charValue.Morale2 ?? 0,
            Slot3 = charValue.Morale3 ?? 0,
            Slot4 = charValue.Morale4 ?? 0,
        });

        // 6. F_PLAYER_HEALTH — HP, AP, morale
        var maxAp = DefaultMaxActionPoints;
        player.ActionPoints = maxAp;
        session.SendPlayerHealth(new PlayerHealthResponse
        {
            Health = player.Health.Current,
            MaxHealth = player.Health.Max,
            ActionPoints = (ushort)player.ActionPoints,
            MaxActionPoints = maxAp,
        });

        // 7. S_PLAYER_LOADED — data-complete marker
        session.SendPlayerLoaded(new PlayerLoadedResponse());

        // 8. F_MAX_VELOCITY (again, matching old server behavior)
        session.SendSpeed(speedPacket);

        // 9. F_PLAYER_STATS (again, matching old server behavior)
        session.SendPlayerStats(statsResponse);

        // F_PLAYER_INIT_COMPLETE and state transition to Playing are handled by the
        // caller (CharacterScreenHandler) after the entity has been placed in the region.

        _logger.LogInformation(
            "Player {Name} ({CharId}, OID {Oid}) init packets sent — awaiting placement",
            player.Name, player.CharacterId, player.ObjectId);
    }

    /// <summary>
    /// Builds a <see cref="PlayerStatsResponse"/> from the player's live
    /// <see cref="StatContainer"/>. Reads primary stats (1–9, 14–16) via
    /// <c>GetTotal</c>, computes derived display stats (10–13) from primary
    /// stats and level, and sets armor from the stat system.
    /// </summary>
    internal static PlayerStatsResponse BuildStatsResponse(PlayerEntity player)
    {
        var stats = player.Stats;
        var level = player.Level;
        var response = new PlayerStatsResponse
        {
            BolsterLevel = level,
            Level = level,
            TacticSlots = DerivedStatFormulas.TacticSlots(level),
            Armor = ClampUshort(stats.GetTotal(StatId.Armor)),
        };

        // Stats 1–9: primary attributes (Strength..Intelligence) — direct from stat system.
        for (byte i = 0; i < 9; i++)
        {
            var statId = (StatId)(i + 1);
            response.SetStat(i, (byte)(i + 1), ClampUshort(stats.GetTotal(statId)));
        }

        // Stats 10–13: derived display skills (Block, Parry, Evade, Disrupt).
        // Computed from primary stats and effective level using V1 formulas.
        response.SetStat(9, (byte)StatId.BlockSkill,
            DerivedStatFormulas.BlockSkill(0, level)); // shield armor deferred — 0 for now
        response.SetStat(10, (byte)StatId.ParrySkill,
            DerivedStatFormulas.ParrySkill(stats.GetTotal(StatId.WeaponSkill), level));
        response.SetStat(11, (byte)StatId.EvadeSkill,
            DerivedStatFormulas.EvadeSkill(stats.GetTotal(StatId.Initiative), level));
        response.SetStat(12, (byte)StatId.DisruptSkill,
            DerivedStatFormulas.DisruptSkill(stats.GetTotal(StatId.Willpower), level));

        // Stats 14–16: resistances — direct from stat system.
        response.SetStat(13, (byte)StatId.SpiritResistance,
            ClampUshort(stats.GetTotal(StatId.SpiritResistance)));
        response.SetStat(14, (byte)StatId.ElementalResistance,
            ClampUshort(stats.GetTotal(StatId.ElementalResistance)));
        response.SetStat(15, (byte)StatId.CorporealResistance,
            ClampUshort(stats.GetTotal(StatId.CorporealResistance)));

        // Stats 17–20: unused/reserved — zero.
        for (byte i = 16; i < 20; i++)
            response.SetStat(i, (byte)(i + 1), 0);

        // Stat 21: hardcoded to 1 (V1 convention).
        response.SetStat(20, 21, 1);

        return response;
    }

    /// <summary>Clamps an int to [0, ushort.MaxValue].</summary>
    private static ushort ClampUshort(int value) =>
        (ushort)Math.Clamp(value, 0, ushort.MaxValue);

    // ═══════════════════════════════════════════════════════════════════
    //  Ability / Career packet helpers
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Sends <c>F_CHARACTER_INFO</c> subtype 1 — the player's ability list with mastery levels.
    /// </summary>
    private static void SendAbilityList(GameSession session, List<ResolvedAbility> abilities)
    {
        var entries = new AbilityLevelEntry[abilities.Count];
        for (var i = 0; i < abilities.Count; i++)
        {
            entries[i] = new AbilityLevelEntry
            {
                Entry = abilities[i].Entry,
                MasteryLevel = abilities[i].MasteryLevel,
            };
        }

        session.SendAbilityList(new AbilityListResponse
        {
            Entries = entries,
        });
    }

    /// <summary>
    /// Builds the tactic slots array from character value (non-null, non-zero entries).
    /// </summary>
    internal static ushort[] BuildTacticsList(Data.Entities.CharacterValue charValue)
    {
        var tactics = new List<ushort>(4);
        if (charValue.Tactic1 is > 0) tactics.Add(charValue.Tactic1.Value);
        if (charValue.Tactic2 is > 0) tactics.Add(charValue.Tactic2.Value);
        if (charValue.Tactic3 is > 0) tactics.Add(charValue.Tactic3.Value);
        if (charValue.Tactic4 is > 0) tactics.Add(charValue.Tactic4.Value);
        return tactics.ToArray();
    }

    /// <summary>
    /// Sends career ability package packets for all applicable career trees.
    /// <para>
    /// Groups core abilities by their database <c>category</c> field (maps to
    /// <c>GameData.CareerCategory</c> tree IDs 0–8, 16–19) and sends one
    /// <c>F_CAREER_CATEGORY</c> header + N × <c>F_CAREER_PACKAGE_INFO</c> per tree.
    /// Trees 7 (mastery) and 9–15 (renown) are handled by separate methods.
    /// </para>
    /// </summary>
    private void SendCareerPackages(GameSession session, byte careerLine)
    {
        var coreAbilities = _gameDataStore.Abilities.GetCoreAbilities(careerLine);

        // Group abilities by their DB category (tree ID).
        // Skip mastery (7), renown (9–15), and out-of-range values like
        // category 24 (morale flag) which don't map to a career tree.
        var groups = new SortedDictionary<byte, List<AbilityDefinition>>();

        foreach (var def in coreAbilities)
        {
            var cat = def.Category;
            if (cat == 7 || (cat >= 9 && cat <= 15) || cat > 19)
                continue;

            if (!groups.TryGetValue(cat, out var list))
                groups[cat] = list = [];
            list.Add(def);
        }

        foreach (var (treeId, entries) in groups)
        {
            var treeName = GetTreeName(treeId, careerLine);
            SendCareerTree(session, careerLine, treeId, entries, treeName);
        }
    }

    /// <summary>
    /// Returns the display name for a career category tree, matching the naming
    /// convention observed in legacy sniff data (e.g. "Ironbreaker Abilities",
    /// "Tank Tactic", "Dwarf Tactics").
    /// </summary>
    private static string GetTreeName(byte treeId, byte careerLine)
    {
        var career = CareerInfo.GetCareerName(careerLine);
        return treeId switch
        {
            0 => $"{career} Abilities",
            1 => $"{career} Tactics",
            2 => $"{career} Morale",
            3 => $"{CareerInfo.GetRoleName(careerLine)} Tactic",
            4 => $"{CareerInfo.GetRoleName(careerLine)} Morale",
            5 => $"{CareerInfo.GetRaceName(careerLine)} Tactics",
            6 => $"{CareerInfo.GetRaceName(careerLine)} Morale",
            8 => $"{career} Auto",
            16 => "Tome Tactic CC A",
            17 => "Tome Tactic CC B",
            19 => $"{career} Starting CC",
            _ => $"Tree {treeId}",
        };
    }

    /// <summary>
    /// Sends a single career tree: one <c>F_CAREER_CATEGORY</c> header followed by
    /// <c>F_CAREER_PACKAGE_INFO</c> for each ability in the tree.
    /// </summary>
    private void SendCareerTree(
        GameSession session, byte careerLine, byte treeId,
        List<AbilityDefinition> entries, string treeName)
    {
        if (entries.Count == 0)
            return;

        // F_CAREER_CATEGORY header
        var slotEntries = new CareerCategorySlotEntry[entries.Count];
        for (var i = 0; i < entries.Count; i++)
            slotEntries[i] = new CareerCategorySlotEntry { Index = (byte)(i + 1) };

        session.SendCareerCategory(new CareerCategoryResponse
        {
            TreeId = treeId,
            TreeName = treeName,
            Slots = slotEntries,
        });

        // F_CAREER_PACKAGE_INFO per ability
        for (var i = 0; i < entries.Count; i++)
        {
            var def = entries[i];
            var entryIndex = (ushort)(i + 1);

            // OptionalValue = floor(91 × BrowserRow² / 20) — a Y-position
            // mapping for the client's ability browser UI, reverse-engineered
            // from sniff data across all 22 Ironbreaker tree-0 entries.
            var browserRow = (def.MinimumRank + 3) / 2;
            session.SendCareerAbilityInfo(new CareerAbilityResponse
            {
                TreeId = treeId,
                EntryIndex = entryIndex,
                MinimumRank = def.MinimumRank,
                CashCost = def.CashCost,
                PackageId = CareerInfo.ComputePackageId(def.Entry, careerLine),
                ReferenceId = def.Entry,
                AbilityName = def.Name,
            });
        }
    }

    /// <summary>
    /// Sends the mastery tree initialization packets:
    /// <list type="number">
    ///   <item>1× <c>F_CAREER_CATEGORY</c> (0xEE) — tree header with 24 slots</item>
    ///   <item>3× <c>F_CAREER_PACKAGE_INFO</c> (0xF3) — points per tree</item>
    ///   <item>21× <c>F_CAREER_PACKAGE_INFO</c> (0xF3) — individual skill entries</item>
    ///   <item>3× <c>F_CAREER_PACKAGE_UPDATE</c> (0xF1) — point summaries</item>
    /// </list>
    /// </summary>
    private void SendMasteryTree(GameSession session, byte careerLine, byte level, MasteryState mastery)
    {
        var masterySlots = _abilityResolver.GetMasterySlots(careerLine, mastery);
        var totalSpent = mastery.TotalPointsSpent;
        var totalAvailable = ComputeTotalMasteryPoints(level);
        var unspent = Math.Max(0, totalAvailable - totalSpent);
        var respecCost = (uint)(totalSpent * 2000);

        // 1. F_CAREER_CATEGORY — mastery tree header (treeId = 7)
        var slotEntries = new CareerCategorySlotEntry[24];
        for (byte i = 0; i < 24; i++)
            slotEntries[i] = new CareerCategorySlotEntry { Index = (byte)(i + 1) };

        session.SendCareerCategory(new CareerCategoryResponse
        {
            TreeId = 7,
            PointsSpent = (byte)Math.Min(totalSpent + unspent, 255),
            PointsAvailable = (byte)Math.Min(unspent, 255),
            RespecCost = respecCost,
            TreeName = $"{CareerInfo.GetCareerName(careerLine)} Spec",
            Slots = slotEntries,
        });

        // 2. F_CAREER_PACKAGE_INFO — tree point counts (3 packets)
        for (byte tree = 0; tree < MasteryState.TreeCount; tree++)
        {
            var pts = mastery.GetTreePoints(tree);
            session.SendCareerPackageInfo(new MasteryTreePointsResponse
            {
                Position = (byte)(tree + 1),
                PointsSpent = Math.Min(pts, (byte)15),
                Visual2 = (byte)(0x0D + tree),
                TreeVisualFlag = tree == 2 ? (byte)0xFC : (byte)0x0F,
            });
        }

        // 3. F_CAREER_PACKAGE_INFO — individual mastery skill entries (up to 21 packets)
        // Slot positions start at 4 (after the 3 tree-point entries)
        foreach (var slot in masterySlots)
        {
            var position = (byte)(slot.TreeIndex * MasteryState.SlotsPerTree + slot.SlotIndex + 4);
            session.SendCareerPackageInfo(new MasterySkillResponse
            {
                Position = position,
                IsActive = slot.IsActive ? (byte)1 : (byte)0,
                AbilityEntry = slot.Definition.Entry,
                AbilityName = slot.Definition.Name,
                TreeNumber = (byte)(slot.TreeIndex + 1),
                PointCost = slot.Definition.PointCost,
            });
        }

        // 4. F_CAREER_PACKAGE_UPDATE — point summaries (3 packets)
        SendMasteryPointsUpdate(session, mastery, unspent, respecCost);
    }

    /// <summary>
    /// Sends <c>F_CAREER_PACKAGE_UPDATE</c> for each mastery tree.
    /// </summary>
    private static void SendMasteryPointsUpdate(
        GameSession session, MasteryState mastery, int unspent, uint respecCost)
    {
        for (byte tree = 0; tree < MasteryState.TreeCount; tree++)
        {
            session.SendCareerPackageUpdate(new CareerPackageUpdateResponse
            {
                TreeId = 7,
                PointsAvailable = (byte)Math.Min(unspent, 255),
                TreeIndex = (byte)(tree + 1),
                PointsSpent = mastery.GetTreePoints(tree),
                RespecCost = respecCost,
            });
        }
    }

    /// <summary>
    /// V1 formula: mastery points unlock after level 10.
    /// 1 point per level from 11–40 = 30 points maximum.
    /// </summary>
    internal static int ComputeTotalMasteryPoints(byte level) =>
        level <= 10 ? 0 : level - 10;
}
