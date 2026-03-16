namespace Core.Infrastructure.Network;

/// <summary>
/// Wraps an RPC handler's response, allowing handlers to explicitly signal
/// "no response should be sent" for early-exit paths (e.g. disconnect, error).
/// Use <see cref="NoResponse"/> for early exits and return the value directly
/// for the happy path (via implicit conversion).
/// </summary>
/// <typeparam name="T">The response payload type.</typeparam>
public readonly struct RpcResult<T>
{
    /// <summary>The response value, if any.</summary>
    public T? Value { get; }

    /// <summary>True if a response should be sent to the client.</summary>
    public bool HasValue { get; }

    private RpcResult(T value)
    {
        Value = value;
        HasValue = true;
    }

    /// <summary>
    /// Indicates that no response should be sent to the client.
    /// Use this for early-exit paths where the handler has already sent
    /// a custom response or initiated a disconnect.
    /// </summary>
    public static RpcResult<T> NoResponse => default;

    /// <summary>
    /// Implicit conversion from a response value to <see cref="RpcResult{T}"/>.
    /// Allows returning the response directly from handler methods without wrapping.
    /// </summary>
    public static implicit operator RpcResult<T>(T value) => new(value);
}
