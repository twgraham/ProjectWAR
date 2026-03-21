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

    // ── World Loading ───────────────────────────────────────────────────

    /// <summary>Sends <c>F_SET_TIME</c> (0xD6) — in-game clock.</summary>
    public static void SendSetTime(this GameSession session, SetTimeResponse response)
        => session.Send((byte)Opcodes.F_SET_TIME, response);

    /// <summary>Sends <c>S_WORLD_SENT</c> (0x83) — final render signal.</summary>
    public static void SendWorldSent(this GameSession session, WorldSentResponse response)
        => session.Send((byte)Opcodes.S_WORLD_SENT, response);
}
