using System.Diagnostics.CodeAnalysis;

namespace Core.Infrastructure.Network;

/// <summary>
/// Provides context about the current connection. Passed to RPC handler methods as a parameter.
/// Similar in concept to gRPC's ServerCallContext or ASP.NET Core's HttpContext.
/// </summary>
public interface IConnectionContext
{
    /// <summary>
    /// Gets the remote endpoint address of the connection (e.g. "127.0.0.1:54321").
    /// </summary>
    string? RemoteAddress { get; }
    
    /// <summary>
    /// The packet framer associated with this connection, used for serializing and framing response packets.
    /// </summary>
    IPacketFramer PacketFramer { get; }

    /// <summary>
    /// Sends a serialized response packet to the connected client.
    /// Thread-safe: backed by a channel-based send queue.
    /// </summary>
    /// <typeparam name="T">The response payload type.</typeparam>
    /// <param name="opcode">The response opcode.</param>
    /// <param name="response">The response object to serialize and send.</param>
    void SendResponse<T>(byte opcode, T response);

    /// <summary>
    /// Disconnects the client with the specified reason.
    /// </summary>
    /// <param name="reason">The reason for disconnection.</param>
    /// <param name="flush">
    /// When <c>true</c>, all previously enqueued packets are sent before the connection is closed
    /// (graceful shutdown). When <c>false</c> (default), the connection is torn down immediately.
    /// </param>
    void Disconnect(string reason, bool flush = false);

    /// <summary>
    /// Gets a key/value collection for storing per-connection state (e.g. auth tokens, session data).
    /// Thread-safe: backed by a ConcurrentDictionary.
    /// </summary>
    IDictionary<string, object> Items { get; }

    /// <summary>
    /// Reports a handler dispatch error. Called by generated code for async handler failures.
    /// Increments the error count and disconnects if the threshold is exceeded.
    /// </summary>
    /// <param name="opcode">The opcode whose handler threw.</param>
    /// <param name="exception">The exception that occurred.</param>
    void OnDispatchError(byte opcode, Exception exception);
    
    TItem Get<TItem>(string key) => (TItem)Items[key];
    
    bool TryGetValue<TItem>(string key, [NotNullWhen(true)] out TItem? value)
    {
        var result = Items.TryGetValue(key, out var obj);
        
        if (!result || obj is not TItem item) {
            value = default!;
            return false;
        }

        value = item;
        return true;
    }
}
