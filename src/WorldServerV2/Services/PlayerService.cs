using System.Collections.Concurrent;
using Core.GameWorld.Entities;
using Core.Session;
using Microsoft.Extensions.Logging;

namespace WorldServerV2.Services;

/// <summary>
/// Manages the mapping between <see cref="GameSession"/> instances and their active
/// <see cref="PlayerEntity"/> player entities. Replaces the legacy static <c>Player._Players</c>
/// and <c>Player.PlayersByCharId</c> dictionaries with an injectable, thread-safe service.
/// <para>
/// <b>Thread-safety model:</b> Identical to <see cref="SessionRegistry"/> — a single
/// <see cref="Lock"/> serializes all compound mutations (<see cref="Bind"/>, <see cref="Unbind"/>),
/// while reads are lock-free through <see cref="ConcurrentDictionary{TKey,TValue}"/>.
/// Mutations are infrequent (world enter / char select / disconnect) relative to reads
/// (every packet handler, region tick, visibility check), so the lock has negligible contention.
/// </para>
/// Registered as a <b>singleton</b> in the DI container.
/// </summary>
public sealed class PlayerService : ISessionResolver<PlayerEntity>
{
    private readonly ConcurrentDictionary<ushort, PlayerEntity> _bySessionId = new();
    private readonly ConcurrentDictionary<uint, PlayerEntity> _byCharacterId = new();
    private readonly ConcurrentDictionary<uint, GameSession> _sessionByCharId = new();
    private readonly ILogger<PlayerService> _logger;
    private readonly Lock _writeLock = new();

    public PlayerService(ILogger<PlayerService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── Write operations (serialized) ───────────────────────────────────

    /// <summary>
    /// Binds a <see cref="PlayerEntity"/> to a <see cref="GameSession"/> when the player enters the world.
    /// <para>
    /// If the session already has a bound player (e.g. character switch without explicit unbind),
    /// the previous player is unbound first. If the character ID is already bound to a different
    /// session (shouldn't happen in normal flow), that binding is displaced and a warning is logged.
    /// </para>
    /// </summary>
    /// <param name="session">The session that owns this player.</param>
    /// <param name="player">The player entity entering the world.</param>
    /// <exception cref="ArgumentNullException">If either argument is null.</exception>
    public void Bind(GameSession session, PlayerEntity player)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(player);

        lock (_writeLock)
        {
            // If this session already has a player (character switch), unbind the old one first.
            if (_bySessionId.TryGetValue(session.Id, out var existingPlayer) && existingPlayer != player)
            {
                _byCharacterId.TryRemove(
                    new KeyValuePair<uint, PlayerEntity>(existingPlayer.CharacterId, existingPlayer));

                _logger.LogDebug(
                    "Session {SessionId} switching character from {OldCharId} to {NewCharId}",
                    session.Id, existingPlayer.CharacterId, player.CharacterId);
            }

            // If this character ID is already bound to a different session, displace it.
            if (_byCharacterId.TryGetValue(player.CharacterId, out var displacedPlayer)
                && displacedPlayer != player)
            {
                // Find and remove the displaced player's session binding.
                foreach (var kvp in _bySessionId)
                {
                    if (kvp.Value == displacedPlayer)
                    {
                        _bySessionId.TryRemove(kvp);
                        break;
                    }
                }

                _logger.LogWarning(
                    "Character {CharId} was bound to another session; displacing",
                    player.CharacterId);
            }

            _bySessionId[session.Id] = player;
            _byCharacterId[player.CharacterId] = player;
            _sessionByCharId[player.CharacterId] = session;
        }

        _logger.LogDebug(
            "Bound Player {CharName} ({CharId}) to Session {SessionId}",
            player.Name, player.CharacterId, session.Id);
    }

    /// <summary>
    /// Unbinds the player from a session (char select, logout, disconnect).
    /// Safe to call if no player is bound — the call is a no-op.
    /// </summary>
    /// <param name="session">The session to unbind from.</param>
    /// <returns>The unbound player entity, or <c>null</c> if none was bound.</returns>
    public PlayerEntity? Unbind(GameSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        PlayerEntity? player;

        lock (_writeLock)
        {
            if (!_bySessionId.TryRemove(session.Id, out player))
                return null;

            // Only remove from char index if it still points to this player.
            _byCharacterId.TryRemove(
                new KeyValuePair<uint, PlayerEntity>(player.CharacterId, player));
            _sessionByCharId.TryRemove(
                new KeyValuePair<uint, GameSession>(player.CharacterId, session));
        }

        _logger.LogDebug(
            "Unbound Player {CharName} ({CharId}) from Session {SessionId}",
            player.Name, player.CharacterId, session.Id);

        return player;
    }

    // ── Lock-free reads ─────────────────────────────────────────────────

    /// <summary>
    /// Gets the <see cref="PlayerEntity"/> bound to the given session, or <c>null</c> if
    /// the session has no active player (pre-world, char screen, etc.).
    /// </summary>
    public PlayerEntity? GetPlayer(GameSession session)
        => _bySessionId.GetValueOrDefault(session.Id);

    /// <summary>
    /// Gets the <see cref="PlayerEntity"/> by character ID, or <c>null</c> if not in world.
    /// </summary>
    public PlayerEntity? GetPlayerByCharacterId(uint characterId)
        => _byCharacterId.GetValueOrDefault(characterId);

    /// <summary>
    /// Gets the <see cref="PlayerEntity"/> by character name (case-insensitive linear scan).
    /// Use <see cref="GetPlayerByCharacterId"/> for O(1) lookups where possible.
    /// </summary>
    public PlayerEntity? GetPlayerByName(string name)
    {
        foreach (var player in _byCharacterId.Values)
        {
            if (name.Equals(player.Name, StringComparison.OrdinalIgnoreCase))
                return player;
        }

        return null;
    }

    /// <inheritdoc />
    public GameSession? GetSession(PlayerEntity player)
        => _sessionByCharId.GetValueOrDefault(player.CharacterId);

    /// <summary>The number of players currently in the world.</summary>
    public int Count => _bySessionId.Count;

    /// <summary>
    /// Enumerates all currently bound player entities. The sequence is a point-in-time snapshot
    /// of the dictionary values.
    /// </summary>
    public IEnumerable<PlayerEntity> OnlinePlayers => _byCharacterId.Values;
}
