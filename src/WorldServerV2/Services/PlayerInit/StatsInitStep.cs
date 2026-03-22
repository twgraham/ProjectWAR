using WorldServerV2.Network;
using WorldServerV2.Network.Dtos;
using WorldServerV2.World.Entities;

namespace WorldServerV2.Services.PlayerInit;

/// <summary>
/// Sends <c>F_PLAYER_STATS</c> — base stats and level information.
/// When the combat system is implemented, this will delegate to a real stats service.
/// </summary>
public sealed class StatsInitStep : IPlayerInitStep
{
    public void Execute(PlayerEntity player, GameSession session)
    {
        var level = player.Level;
        var response = new PlayerStatsResponse
        {
            BolsterLevel = level,
            Level = level,
            TacticSlots = level > 10 ? (byte)(level / 10) : (byte)0,
        };

        // Stat IDs 0–20 (zero-based, matching legacy F_PLAYER_STATS), all zeroed for now.
        for (byte i = 0; i < 21; i++)
        {
            response.SetStat(i, i, 0);
        }

        session.SendPlayerStats(response);
    }
}
