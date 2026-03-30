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
    public static void SendPlayerInitted(this GameSession session, PlayerInittedResponse response)
        => session.Send((byte)Opcodes.S_PLAYER_INITTED, response);

    /// <summary>Sends <c>F_BAG_INFO</c> (0x95) — inventory/bank capacity and expansion costs.</summary>
    public static void SendBagInfo(this GameSession session, BagInfoResponse response)
        => session.Send((byte)Opcodes.F_BAG_INFO, response);

    /// <summary>Sends <c>F_GET_ITEM</c> (0xAA) — a batch of inventory items (max 255 per packet).</summary>
    public static void SendGetItem(this GameSession session, GetItemResponse response)
        => session.Send((byte)Opcodes.F_GET_ITEM, response);

    /// <summary>Sends <c>F_PLAYER_STATS</c> (0x46) — base stats and level.</summary>
    public static void SendPlayerStats(this GameSession session, PlayerStatsResponse response)
        => session.Send((byte)Opcodes.F_PLAYER_STATS, response);

    /// <summary>Sends <c>F_PLAYER_HEALTH</c> (0x05) — HP, AP, morale.</summary>
    public static void SendPlayerHealth(this GameSession session, PlayerHealthResponse response)
        => session.Send((byte)Opcodes.F_PLAYER_HEALTH, response);

    /// <summary>Sends <c>F_MAX_VELOCITY</c> (0x1E) — movement speed.</summary>
    public static void SendSpeed(this GameSession session, SpeedResponse response)
        => session.Send((byte)Opcodes.F_MAX_VELOCITY, response);

    /// <summary>Sends <c>S_PLAYER_LOADED</c> (0x89) — data-complete marker.</summary>
    public static void SendPlayerLoaded(this GameSession session, PlayerLoadedResponse response)
        => session.Send((byte)Opcodes.S_PLAYER_LOADED, response);

    /// <summary>Sends <c>F_PLAYER_INIT_COMPLETE</c> (0xEF) — init-done signal.</summary>
    public static void SendPlayerInitComplete(this GameSession session, PlayerInitCompleteResponse response)
        => session.Send((byte)Opcodes.F_PLAYER_INIT_COMPLETE, response);

    // ── Abilities / Career ──────────────────────────────────────────────

    /// <summary>Sends <c>F_CHARACTER_INFO</c> (0xBE) subtype 3 — skill list, race, rally point.</summary>
    public static void SendSkillList(this GameSession session, SkillListResponse response)
        => session.Send((byte)Opcodes.F_CHARACTER_INFO, response);

    /// <summary>Sends <c>F_CHARACTER_INFO</c> (0xBE) subtype 1 — ability list with mastery levels.</summary>
    public static void SendAbilityList(this GameSession session, AbilityListResponse response)
        => session.Send((byte)Opcodes.F_CHARACTER_INFO, response);

    /// <summary>Sends <c>F_MORALE_LIST</c> (0x8C) — 4 morale ability slots.</summary>
    public static void SendMoraleList(this GameSession session, MoraleListResponse response)
        => session.Send((byte)Opcodes.F_MORALE_LIST, response);

    /// <summary>Sends <c>F_TACTICS</c> (0xF7) — active tactic abilities.</summary>
    public static void SendTactics(this GameSession session, TacticsResponse response)
        => session.Send((byte)Opcodes.F_TACTICS, response);

    /// <summary>Sends <c>F_CAREER_CATEGORY</c> (0xEE) — tree header for mastery or renown.</summary>
    public static void SendCareerCategory(this GameSession session, CareerCategoryResponse response)
        => session.Send((byte)Opcodes.F_CAREER_CATEGORY, response);

    /// <summary>Sends <c>F_CAREER_PACKAGE_INFO</c> (0xF3) — career ability/tactic entry.</summary>
    public static void SendCareerAbilityInfo(this GameSession session, CareerAbilityResponse response)
        => session.Send((byte)Opcodes.F_CAREER_PACKAGE_INFO, response);

    /// <summary>Sends <c>F_CAREER_PACKAGE_INFO</c> (0xF3) — mastery tree point count.</summary>
    public static void SendCareerPackageInfo(this GameSession session, MasteryTreePointsResponse response)
        => session.Send((byte)Opcodes.F_CAREER_PACKAGE_INFO, response);

    /// <summary>Sends <c>F_CAREER_PACKAGE_INFO</c> (0xF3) — individual mastery skill entry.</summary>
    public static void SendCareerPackageInfo(this GameSession session, MasterySkillResponse response)
        => session.Send((byte)Opcodes.F_CAREER_PACKAGE_INFO, response);

    /// <summary>Sends <c>F_CAREER_PACKAGE_UPDATE</c> (0xF1) — mastery/renown point summary per tree.</summary>
    public static void SendCareerPackageUpdate(this GameSession session, CareerPackageUpdateResponse response)
        => session.Send((byte)Opcodes.F_CAREER_PACKAGE_UPDATE, response);

    // ── World Loading ───────────────────────────────────────────────────

    /// <summary>Sends <c>F_SET_TIME</c> (0xD6) — in-game clock.</summary>
    public static void SendSetTime(this GameSession session, SetTimeResponse response)
        => session.Send((byte)Opcodes.F_SET_TIME, response);

    /// <summary>Sends <c>S_WORLD_SENT</c> (0x83) — final render signal.</summary>
    public static void SendWorldSent(this GameSession session, WorldSentResponse response)
        => session.Send((byte)Opcodes.S_WORLD_SENT, response);
}
