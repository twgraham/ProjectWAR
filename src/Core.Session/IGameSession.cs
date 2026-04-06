namespace Core.Session;

public interface IGameSession
{
    /// <summary>
    /// Unique session identifier, assigned by <see cref="SessionRegistry"/>.
    /// </summary>
    ushort Id { get; }

    /// <summary>
    /// The current state of this client session.
    /// </summary>
    ClientState State { get; }
    
    event EventHandler<ClientState> OnClientStateChanged; 
    
    /// <summary>
    /// The remote endpoint address (e.g. "127.0.0.1:54321"), or <c>null</c> if disconnected.
    /// </summary>
    string? RemoteAddress { get; }

    /// <summary>
    /// Sends a serialized packet to the client.
    /// Thread-safe — backed by the connection's channel-based send queue.
    /// Safe to call from region update threads, handler threads, or any other context.
    /// </summary>
    /// <typeparam name="T">The response payload type.</typeparam>
    /// <param name="opcode">The response opcode.</param>
    /// <param name="packet">The response object to serialize and send.</param>
    void Send<T>(byte opcode, T packet);

    /// <summary>
    /// Disconnects the client.
    /// </summary>
    /// <param name="reason">Human-readable reason for logging.</param>
    /// <param name="flush">
    /// When <c>true</c>, all previously queued outbound packets are drained before the
    /// socket is closed. When <c>false</c> (default), the connection is torn down immediately.
    /// </param>
    void Disconnect(string reason, bool flush = false);
}