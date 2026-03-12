using Core.Infrastructure.Network;
using WorldServerV2.Network.Dtos;

namespace WorldServerV2.Network;

// Source-generated fast-path serializers for all binary DTOs.
[PacketSerializerContext(
    typeof(EncryptKeyRequest),
    typeof(EncryptKeyResponse),
    typeof(ConnectRequest),
    typeof(ConnectResponse),
    typeof(PingRequest),
    typeof(PingResponse),
    typeof(PlayerEnterRequest),
    typeof(PlayerEnterResponse),
    typeof(PlayerExitRequest),
    typeof(PlayerQuitResponse),
    typeof(RequestCharacterRequest),
    typeof(RequestCharacterResponse),
    typeof(RequestCharacterErrorResponse),
    typeof(DumpArenasLargeRequest),
    typeof(WorldEnterResponse))]
public partial class GameServerContext : IPacketSerializerContext
{
}