using WorldServerV2.Network;
using WorldServerV2.World.Entities;

namespace WorldServerV2.Services.PlayerInit;

/// <summary>
/// A single step in the player initialization sequence. Each step reads from
/// the fully-initialized <see cref="PlayerEntity"/> and sends one or more
/// packets to the client via <see cref="GameSession"/>.
/// <para>
/// Steps are executed in registration order by <see cref="PlayerInitPipeline"/>.
/// Implementations should be stateless singletons — all per-call state flows
/// through the <paramref name="player"/> and <paramref name="session"/> parameters.
/// </para>
/// </summary>
public interface IPlayerInitStep
{
    /// <summary>
    /// Sends the packets for this initialization step.
    /// </summary>
    /// <param name="player">The player entity with state already initialized.</param>
    /// <param name="session">The network session for sending packets.</param>
    void Execute(PlayerEntity player, GameSession session);
}
