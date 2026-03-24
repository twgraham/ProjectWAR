using Microsoft.Extensions.Logging;
using WorldServerV2.Data;
using WorldServerV2.Network;
using WorldServerV2.Network.Dtos;
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

    public PlayerInitPipeline(
        ILogger<PlayerInitPipeline> logger,
        RealmInfo realmInfo,
        IGameDataStore gameDataStore)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _realmInfo = realmInfo ?? throw new ArgumentNullException(nameof(realmInfo));
        _gameDataStore = gameDataStore ?? throw new ArgumentNullException(nameof(gameDataStore));
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

        // 3. F_PLAYER_STATS — base stats (minimal: all zeros except level)
        var statsResponse = BuildStatsResponse(player);
        session.SendPlayerStats(statsResponse);

        // 4. F_PLAYER_HEALTH — HP, AP, morale
        var maxAp = DefaultMaxActionPoints;
        player.ActionPoints = maxAp;
        session.SendPlayerHealth(new PlayerHealthResponse
        {
            Health = player.Health.Current,
            MaxHealth = player.Health.Max,
            ActionPoints = (ushort)player.ActionPoints,
            MaxActionPoints = maxAp,
        });

        // 5. S_PLAYER_LOADED — data-complete marker
        session.SendPlayerLoaded(new PlayerLoadedResponse());

        // 6. F_MAX_VELOCITY (again, matching old server behavior)
        session.SendSpeed(speedPacket);

        // 7. F_PLAYER_STATS (again, matching old server behavior)
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
}
