namespace FrameWork.NetWork.V4;

/// <summary>
/// Lightweight base class for server-side RPC packet handlers.
/// Subclass this and add methods decorated with <see cref="RpcAttribute"/> to handle incoming packets.
/// Dependencies can be injected via the constructor (connection-scoped) or
/// via <see cref="FromServicesAttribute"/> on method parameters (packet-scoped).
/// </summary>
public abstract class PacketHandler
{
}
