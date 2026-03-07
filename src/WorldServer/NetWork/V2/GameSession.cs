using Core.Infrastructure.Network;

namespace WorldServer.NetWork.V2;

/// <summary>
/// Represents an authenticated client session, mediating between the transport layer
/// and game state (Account, Player, etc.).
/// <para>
/// Game code interacts exclusively through this class — it never touches the underlying
/// <see cref="IConnectionContext"/> directly. Outbound packets flow through <see cref="Send{T}"/>,
/// which enqueues onto the connection's channel-based send queue (thread-safe from any thread,
/// including region update threads).
/// </para>
/// <para>
/// Created by <see cref="SessionRegistry"/> when a connection is accepted.
/// Torn down on disconnect via <see cref="SessionRegistry.Remove"/>.
/// </para>
/// </summary>
public sealed class GameSession
{
    /// <summary>
    /// Key used to store the session in <see cref="IConnectionContext.Items"/>
    /// so packet handlers can retrieve it via the <c>context.Session</c> extension property.
    /// </summary>
    internal const string ItemKey = "GameSession";

    private readonly IConnectionContext _connection;

    internal GameSession(ushort sessionId, IConnectionContext connection)
    {
        Id = sessionId;
        _connection = connection;
        State = eClientState.Connecting;
    }

    /// <summary>
    /// Unique session identifier, assigned by <see cref="SessionRegistry"/>.
    /// </summary>
    public ushort Id { get; }

    /// <summary>
    /// The current state of this client session.
    /// </summary>
    public eClientState State { get; internal set; }

    /// <summary>
    /// The authenticated account, or <c>null</c> if not yet authenticated.
    /// Set via <see cref="SessionRegistry.SetSessionAccount"/>.
    /// </summary>
    public AccountInfo? Account { get; internal set; }

    /// <summary>
    /// The remote endpoint address (e.g. "127.0.0.1:54321"), or <c>null</c> if disconnected.
    /// </summary>
    public string? RemoteAddress => _connection.RemoteAddress;

    /// <summary>
    /// Sends a serialized packet to the client.
    /// Thread-safe — backed by the connection's channel-based send queue.
    /// Safe to call from region update threads, handler threads, or any other context.
    /// </summary>
    /// <typeparam name="T">The response payload type.</typeparam>
    /// <param name="opcode">The response opcode.</param>
    /// <param name="packet">The response object to serialize and send.</param>
    public void Send<T>(byte opcode, T packet)
        => _connection.SendResponse(opcode, packet);

    /// <summary>
    /// Disconnects the client.
    /// </summary>
    /// <param name="reason">Human-readable reason for logging.</param>
    /// <param name="flush">
    /// When <c>true</c>, all previously queued outbound packets are drained before the
    /// socket is closed. When <c>false</c> (default), the connection is torn down immediately.
    /// </param>
    public void Disconnect(string reason, bool flush = false)
        => _connection.Disconnect(reason, flush);
}
