using WorldServerV2.Network;
using WorldServerV2.World.Entities;

namespace WorldServerV2.Services;

/// <summary>
/// Resolves the active <see cref="GameSession"/> for a given <see cref="PlayerEntity"/>.
/// <para>
/// This abstraction decouples the world/game layer from direct knowledge of the network
/// session registry. The region tick thread uses it to deliver visibility notifications
/// without the entity itself carrying a reference to its session.
/// </para>
/// </summary>
public interface ISessionResolver
{
    /// <summary>
    /// Returns the <see cref="GameSession"/> currently bound to
    /// <paramref name="player"/>, or <c>null</c> if the player has no active session
    /// (e.g. during logout or before world-enter completes).
    /// </summary>
    GameSession? GetSession(PlayerEntity player);
}
