using Core.Infrastructure.Network;
using Core.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WorldServerV2.Network;

namespace WorldServerV2.Services;

/// <summary>
/// Wires <see cref="NetworkManager"/> connection lifecycle events to session and player
/// management: creating <see cref="GameSession"/> instances on connect, and performing
/// the full teardown sequence on disconnect (unbind player, remove from world, destroy session).
/// <para>
/// Registered as an <see cref="IHostedService"/> so the host eagerly constructs and starts
/// it during application startup — before <see cref="NetworkManager"/> begins accepting
/// connections.
/// </para>
/// <para>
/// <b>Disconnect sequence</b> (all on the network I/O thread that detected the disconnect):
/// <list type="number">
///   <item><see cref="GameSession.MoveToDisconnected"/> — transitions the session state machine.</item>
///   <item><see cref="PlayerService.Unbind"/> — removes the session ↔ player mapping.</item>
///   <item><see cref="WorldService.LeaveWorld"/> — enqueues entity removal on the region tick thread.</item>
///   <item><see cref="SessionRegistry.Remove"/> — returns the session ID to the free pool.</item>
/// </list>
/// Steps 2–3 are O(1) dictionary removes plus a non-blocking channel enqueue.
/// </para>
/// </summary>
public sealed class SessionLifecycleService : IHostedService, IDisposable
{
    private readonly NetworkManager _networkManager;
    private readonly SessionRegistry _sessionRegistry;
    private readonly PlayerService _playerService;
    private readonly WorldService _worldService;
    private readonly ILogger<SessionLifecycleService> _logger;
    private bool _disposed;

    public SessionLifecycleService(
        NetworkManager networkManager,
        SessionRegistry sessionRegistry,
        PlayerService playerService,
        WorldService worldService,
        ILogger<SessionLifecycleService> logger)
    {
        _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        _sessionRegistry = sessionRegistry ?? throw new ArgumentNullException(nameof(sessionRegistry));
        _playerService = playerService ?? throw new ArgumentNullException(nameof(playerService));
        _worldService = worldService ?? throw new ArgumentNullException(nameof(worldService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _networkManager.ClientConnected += OnClientConnected;
        _networkManager.ClientDisconnected += OnClientDisconnected;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _networkManager.ClientConnected -= OnClientConnected;
        _networkManager.ClientDisconnected -= OnClientDisconnected;
        return Task.CompletedTask;
    }

    private void OnClientConnected(IConnectionContext connection)
    {
        var session = _sessionRegistry.CreateSession(connection);

        _logger.LogDebug(
            "Session {SessionId} created for {Address}",
            session.Id, session.RemoteAddress);
    }

    private void OnClientDisconnected(IConnectionContext connection, DisconnectReason reason)
    {
        var session = connection.Session;

        // 1. Transition the session state machine so handlers see Disconnected immediately.
        session.MoveToDisconnected();

        // 2. Unbind the player ↔ session mapping and remove the entity from the world.
        var player = _playerService.Unbind(session);
        if (player is not null && player.ObjectId > 0)
        {
            _worldService.LeaveWorld(player);

            _logger.LogInformation(
                "Player {Name} ({CharId}) removed from world on disconnect (Session {SessionId})",
                player.Name, player.CharacterId, session.Id);
        }

        // 3. Remove the session from the registry, returning the ID to the free pool.
        _sessionRegistry.Remove(session);

        _logger.LogDebug(
            "Session {SessionId} removed. Reason: {Reason}",
            session.Id, reason);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _networkManager.ClientConnected -= OnClientConnected;
        _networkManager.ClientDisconnected -= OnClientDisconnected;
    }
}
