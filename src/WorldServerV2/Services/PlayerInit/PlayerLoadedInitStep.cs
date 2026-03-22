using WorldServerV2.Network;
using WorldServerV2.Network.Dtos;
using WorldServerV2.World.Entities;

namespace WorldServerV2.Services.PlayerInit;

/// <summary>
/// Sends <c>S_PLAYER_LOADED</c> — signals the client that player data is fully loaded.
/// This is the final data-complete marker before the duplicate speed/stats packets.
/// </summary>
public sealed class PlayerLoadedInitStep : IPlayerInitStep
{
    public void Execute(PlayerEntity player, GameSession session)
    {
        session.SendPlayerLoaded(new PlayerLoadedResponse());
    }
}
