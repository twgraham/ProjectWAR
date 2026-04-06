using Core.Session;
using WorldServerV2.Network.Dtos;

namespace WorldServerV2.Network;

/// <summary>
/// Typed extension methods for sending well-known packets through a <see cref="GameSession"/>.
/// <para>
/// Each method encodes a single opcode → DTO mapping, eliminating scattered
/// <c>(byte)Opcodes.XXX</c> casts and ensuring the correct payload type is paired
/// with its opcode at compile time. Game systems and services should prefer these
/// over the raw <see cref="GameSession.Send{T}"/> overload wherever a mapping exists.
/// </para>
/// <para>
/// This class is intentionally free of game-domain types (Character, Account, etc.) —
/// it operates purely on the session transport and serializable DTOs.
/// </para>
/// </summary>
public static class GameSessionPacketExtensions
{
    // ── Player Initialization ───────────────────────────────────────────

    /// <summary>Sends <c>S_PLAYER_INITTED</c> (0x88) — identity, position, realm, career.</summary>
    public static void SendPlayerInitted(this IGameSession session, PlayerInittedResponse response)
        => session.Send((byte)Opcodes.S_PLAYER_INITTED, response);

    /// <summary>Sends <c>F_BAG_INFO</c> (0x95) — inventory/bank capacity and expansion costs.</summary>
    public static void SendBagInfo(this IGameSession session, BagInfoResponse response)
        => session.Send((byte)Opcodes.F_BAG_INFO, response);

    /// <summary>Sends <c>F_GET_ITEM</c> (0xAA) — a batch of inventory items (max 255 per packet).</summary>
    public static void SendGetItem(this IGameSession session, GetItemResponse response)
        => session.Send((byte)Opcodes.F_GET_ITEM, response);

    /// <summary>Sends <c>F_PLAYER_STATS</c> (0x46) — base stats and level.</summary>
    public static void SendPlayerStats(this IGameSession session, PlayerStatsResponse response)
        => session.Send((byte)Opcodes.F_PLAYER_STATS, response);

    /// <summary>Sends <c>F_PLAYER_HEALTH</c> (0x05) — HP, AP, morale.</summary>
    public static void SendPlayerHealth(this IGameSession session, PlayerHealthResponse response)
        => session.Send((byte)Opcodes.F_PLAYER_HEALTH, response);

    /// <summary>Sends <c>F_MAX_VELOCITY</c> (0x1E) — movement speed.</summary>
    public static void SendSpeed(this IGameSession session, SpeedResponse response)
        => session.Send((byte)Opcodes.F_MAX_VELOCITY, response);

    /// <summary>Sends <c>S_PLAYER_LOADED</c> (0x89) — data-complete marker.</summary>
    public static void SendPlayerLoaded(this IGameSession session, PlayerLoadedResponse response)
        => session.Send((byte)Opcodes.S_PLAYER_LOADED, response);

    /// <summary>Sends <c>F_PLAYER_INIT_COMPLETE</c> (0xEF) — init-done signal.</summary>
    public static void SendPlayerInitComplete(this IGameSession session, PlayerInitCompleteResponse response)
        => session.Send((byte)Opcodes.F_PLAYER_INIT_COMPLETE, response);

    // ── Abilities / Career ──────────────────────────────────────────────

    /// <summary>Sends <c>F_CHARACTER_INFO</c> (0xBE) subtype 3 — skill list, race, rally point.</summary>
    public static void SendSkillList(this IGameSession session, SkillListResponse response)
        => session.Send((byte)Opcodes.F_CHARACTER_INFO, response);

    /// <summary>Sends <c>F_CHARACTER_INFO</c> (0xBE) subtype 1 — ability list with mastery levels.</summary>
    public static void SendAbilityList(this IGameSession session, AbilityListResponse response)
        => session.Send((byte)Opcodes.F_CHARACTER_INFO, response);

    /// <summary>Sends <c>F_MORALE_LIST</c> (0x8C) — 4 morale ability slots.</summary>
    public static void SendMoraleList(this IGameSession session, MoraleListResponse response)
        => session.Send((byte)Opcodes.F_MORALE_LIST, response);

    /// <summary>Sends <c>F_TACTICS</c> (0xF7) — active tactic abilities.</summary>
    public static void SendTactics(this IGameSession session, TacticsResponse response)
        => session.Send((byte)Opcodes.F_TACTICS, response);

    /// <summary>Sends <c>F_CAREER_CATEGORY</c> (0xEE) — tree header for mastery or renown.</summary>
    public static void SendCareerCategory(this IGameSession session, CareerCategoryResponse response)
        => session.Send((byte)Opcodes.F_CAREER_CATEGORY, response);

    /// <summary>Sends <c>F_CAREER_PACKAGE_INFO</c> (0xF3) — career ability/tactic entry.</summary>
    public static void SendCareerAbilityInfo(this IGameSession session, CareerAbilityResponse response)
        => session.Send((byte)Opcodes.F_CAREER_PACKAGE_INFO, response);

    /// <summary>Sends <c>F_CAREER_PACKAGE_INFO</c> (0xF3) — mastery tree point count.</summary>
    public static void SendCareerPackageInfo(this IGameSession session, MasteryTreePointsResponse response)
        => session.Send((byte)Opcodes.F_CAREER_PACKAGE_INFO, response);

    /// <summary>Sends <c>F_CAREER_PACKAGE_INFO</c> (0xF3) — individual mastery skill entry.</summary>
    public static void SendCareerPackageInfo(this IGameSession session, MasterySkillResponse response)
        => session.Send((byte)Opcodes.F_CAREER_PACKAGE_INFO, response);

    /// <summary>Sends <c>F_CAREER_PACKAGE_UPDATE</c> (0xF1) — mastery/renown point summary per tree.</summary>
    public static void SendCareerPackageUpdate(this IGameSession session, CareerPackageUpdateResponse response)
        => session.Send((byte)Opcodes.F_CAREER_PACKAGE_UPDATE, response);

    // ── World Loading ───────────────────────────────────────────────────

    /// <summary>Sends <c>F_CREATE_MONSTER</c> (0x72) — notifies client of a visible NPC/creature.</summary>
    public static void SendCreateMonster(this IGameSession session, CreateMonsterResponse response)
        => session.Send((byte)Opcodes.F_CREATE_MONSTER, response);
    
    public static void SendCreatePlayer(this IGameSession session, CreatePlayerResponse response)
        => session.Send((byte)Opcodes.F_CREATE_PLAYER, response);

    /// <summary>Sends <c>F_CREATE_STATIC</c> (0x71) — notifies client of a visible static game object.</summary>
    public static void SendCreateStatic(this IGameSession session, CreateStaticResponse response)
        => session.Send((byte)Opcodes.F_CREATE_STATIC, response);

    /// <summary>Sends <c>F_OBJECT_STATE</c> (0x09) — stationary entity state (position, health, heading).</summary>
    public static void SendObjectState(this IGameSession session, StationaryObjectStateResponse response)
        => session.Send((byte)Opcodes.F_OBJECT_STATE, response);

    /// <summary>Sends <c>F_OBJECT_STATE</c> (0x09) — moving entity state (position, health, speed, destination).</summary>
    public static void SendObjectState(this IGameSession session, MovingObjectStateResponse response)
        => session.Send((byte)Opcodes.F_OBJECT_STATE, response);

    /// <summary>Sends <c>F_PLAYER_INVENTORY</c> (0xBD) — NPC/creature equipped items.</summary>
    public static void SendEquippedInventory(this IGameSession session, EquippedInventoryResponse response)
        => session.Send((byte)Opcodes.F_PLAYER_INVENTORY, response);

    /// <summary>Sends <c>F_SET_TIME</c> (0xD6) — in-game clock.</summary>
    public static void SendSetTime(this IGameSession session, SetTimeResponse response)
        => session.Send((byte)Opcodes.F_SET_TIME, response);

    /// <summary>Sends <c>S_WORLD_SENT</c> (0x83) — final render signal.</summary>
    public static void SendWorldSent(this IGameSession session, WorldSentResponse response)
        => session.Send((byte)Opcodes.S_WORLD_SENT, response);

    // ── Movement Relay ──────────────────────────────────────────────────

    /// <summary>Sends <c>F_PLAYER_STATE2</c> (0x62) — relayed movement state of another player.</summary>
    public static void SendPlayerStateRelay(this IGameSession session, PlayerStateRelayResponse response)
        => session.Send((byte)Opcodes.F_PLAYER_STATE2, response);
}
