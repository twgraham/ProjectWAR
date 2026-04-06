using Core.Infrastructure.Network;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Core.Session;

/// <summary>
/// Wires <see cref="NetworkManager"/> connection lifecycle events to the
/// <see cref="SessionRegistry"/>, creating <see cref="GameSession"/> instances
/// on connect and tearing them down on disconnect.
/// <para>
/// Registered as an <see cref="IHostedService"/> so the host eagerly constructs
/// and starts it during application startup — before <see cref="NetworkManager"/>
/// begins accepting connections.
/// </para>
/// </summary>
public sealed class SessionLifecycleService : IHostedService, IDisposable
{
    private readonly NetworkManager _networkManager;
    private readonly SessionRegistry _sessionRegistry;
    private readonly ILogger<SessionLifecycleService> _logger;
    private bool _disposed;
    
    public SessionLifecycleService(
        NetworkManager networkManager,
        SessionRegistry sessionRegistry,
        ILogger<SessionLifecycleService> logger)
    {
        _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        _sessionRegistry = sessionRegistry ?? throw new ArgumentNullException(nameof(sessionRegistry));
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
        if (!connection.TryGetValue<GameSession>(GameSession.ItemKey, out var session))
        {
            _logger.LogWarning(
                "No session found for disconnected client {Address}",
                connection.RemoteAddress);
            return;
        }

        session.MoveToDisconnected();
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
