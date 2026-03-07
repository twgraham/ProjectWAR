using Core.Infrastructure.Network;
using WorldServer.NetWork.V2;
using WorldServer.NetWork.V2.Dtos;

namespace WorldServer.Tests.Integration;

/// <summary>
/// A minimal <see cref="IPacketDispatcher"/> for integration tests that handles
/// only the packets needed by the smoke test suite. Replaces the source-generated
/// dispatcher (which requires a full compilation of production handlers).
/// <para>
/// Supports:
/// <list type="bullet">
///   <item><c>F_ENCRYPTKEY</c> (0x5C) — sends <c>F_RECEIVE_ENCRYPTKEY</c> (0x8A) with Status=1 when cipher=0</item>
///   <item><c>F_DISCONNECT</c> (0x10) — disconnects the client</item>
/// </list>
/// </para>
/// </summary>
internal sealed class TestPacketDispatcher : IPacketDispatcher
{
    private const byte OpcodeEncryptKey = 0x5C;
    private const byte OpcodeDisconnect = 0x10;
    private const byte OpcodeReceiveEncryptKey = 0x8A;

    /// <summary>
    /// Raised each time a packet is dispatched so tests can observe dispatch events.
    /// Parameters: (opcode, connection).
    /// </summary>
    public event Action<byte, IConnectionContext>? PacketDispatched;

    public void Dispatch(
        byte opcode,
        ReadOnlyMemory<byte> payload,
        IServiceProvider services,
        IPacketSerializer serializer,
        IConnectionContext connection)
    {
        switch (opcode)
        {
            case OpcodeEncryptKey:
                HandleEncryptKey(payload, serializer, connection);
                break;

            case OpcodeDisconnect:
                connection.Disconnect("Client requested disconnect");
                break;
        }

        PacketDispatched?.Invoke(opcode, connection);
    }

    private static void HandleEncryptKey(
        ReadOnlyMemory<byte> payload,
        IPacketSerializer serializer,
        IConnectionContext connection)
    {
        var request = serializer.Deserialize<EncryptKeyRequest>(payload.Span);

        if (request.Cipher == 0)
        {
            connection.SendResponse(OpcodeReceiveEncryptKey, new EncryptKeyResponse { Status = 1 });
        }
        else if (request.Cipher == 1 && connection.PacketFramer is GameServerFramer framer)
        {
            framer.SetEncryptionKey(request.Key);
        }
    }
}
