namespace Core.Session;

/// <summary>
/// Resolves the active <see cref="GameSession"/> for a given entity (e.g. player)
/// <para>
/// This abstraction decouples the world/game layer from direct knowledge of the network
/// session registry. The region tick thread uses it to deliver visibility notifications
/// without the entity itself carrying a reference to its session.
/// </para>
/// </summary>
public interface ISessionResolver<in T>
{
    /// <summary>
    /// Returns the <see cref="GameSession"/> currently bound to
    /// <paramref name="entity"/>, or <c>null</c> if the entity has no active session
    /// (e.g. during logout or before world-enter completes).
    /// </summary>
    GameSession? GetSession(T entity);
}
