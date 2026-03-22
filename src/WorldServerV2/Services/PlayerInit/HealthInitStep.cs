using WorldServerV2.Network;
using WorldServerV2.Network.Dtos;
using WorldServerV2.World.Entities;

namespace WorldServerV2.Services.PlayerInit;

/// <summary>
/// Sends <c>F_PLAYER_HEALTH</c> — HP, action points, and morale.
/// </summary>
public sealed class HealthInitStep : IPlayerInitStep
{
    private const ushort DefaultActionPoints = 250;
    private const ushort DefaultMaxActionPoints = 250;

    public void Execute(PlayerEntity player, GameSession session)
    {
        session.SendPlayerHealth(new PlayerHealthResponse
        {
            Health = player.Health.Current,
            MaxHealth = player.Health.Max,
            ActionPoints = DefaultActionPoints,
            MaxActionPoints = DefaultMaxActionPoints,
        });
    }
}
