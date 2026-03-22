using WorldServerV2.Network;
using WorldServerV2.Network.Dtos;
using WorldServerV2.World.Entities;

namespace WorldServerV2.Services.PlayerInit;

/// <summary>
/// Sends <c>F_MAX_VELOCITY</c> — the player's movement speed.
/// Sent twice during init to match the legacy server behavior.
/// </summary>
public sealed class SpeedInitStep : IPlayerInitStep
{
    public void Execute(PlayerEntity player, GameSession session)
    {
        var charValue = player.Character.Value;
        var speed = charValue.Speed > 0 ? (ushort)charValue.Speed : (ushort)100;

        var packet = new SpeedResponse
        {
            Speed = speed,
            CanMove = (byte)(speed > 0 ? 1 : 0),
            SpeedPercent = 100,
        };

        session.SendSpeed(packet);
    }
}
