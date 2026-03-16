using Core.Infrastructure.Network;
using Core.Infrastructure.Network.Serialization;
using WorldServerV2.Network.Dtos;

namespace WorldServerV2.Network;

// Source-generated fast-path serializers for all binary DTOs.
[PacketSerializerContext(
    typeof(AccountCharacterModifiedResponse),
    typeof(AccountCharacterModifyErrorResponse),
    typeof(CharacterTemplatesRequest),
    typeof(CharacterTemplatesResponse),
    typeof(CheckNameResponse),
    typeof(ConnectRequest),
    typeof(ConnectResponse),
    typeof(CreateCharacterRequest),
    typeof(DeleteCharacterRequest),
    typeof(DeleteNameRequest),
    typeof(DumpArenasLargeRequest),
    typeof(EncryptKeyRequest),
    typeof(EncryptKeyResponse),
    typeof(OpenGameRequest),
    typeof(OpenGameResponse),
    typeof(PingRequest),
    typeof(PingResponse),
    typeof(PlayerEnterRequest),
    typeof(PlayerEnterResponse),
    typeof(PlayerExitRequest),
    typeof(PlayerQuitResponse),
    typeof(RequestCharacterRequest),
    typeof(RequestCharacterResponse),
    typeof(RequestCharacterErrorResponse),
    typeof(WorldEnterResponse))]
public partial class GameServerContext
{
}