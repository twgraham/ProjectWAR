using WorldServerV2.Network;

namespace WorldServerV2.World.Components;

/// <summary>
/// A component that contributes additional packets when its owner becomes visible to a player.
/// <para>
/// When a <see cref="Entities.PlayerEntity"/> first sees a <see cref="Entities.WorldEntity"/>,
/// the region sends the create-packet (<c>F_CREATE_MONSTER</c> / <c>F_CREATE_STATIC</c>) and
/// then iterates the target's components looking for <see cref="IVisibilityInitContributor"/>
/// implementations. Each contributor can send follow-up packets (e.g. <c>F_PLAYER_INVENTORY</c>
/// for equipped items) on the observing player's session.
/// </para>
/// <para>
/// This pattern keeps the <c>Region</c> generic — it doesn't need to know about specific
/// component types, and new contributors can be added by attaching new components.
/// </para>
/// </summary>
public interface IVisibilityInitContributor
{
    /// <summary>
    /// Sends any follow-up packets required to fully initialize this component's visual
    /// state on the given player's client.
    /// </summary>
    /// <param name="session">The observing player's network session.</param>
    void SendVisibilityInit(GameSession session);
}
