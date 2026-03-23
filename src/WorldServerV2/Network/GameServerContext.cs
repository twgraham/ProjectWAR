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
    typeof(InitializePlayerRequest),
    typeof(InterfaceCommandRequest),
    typeof(OpenGameRequest),
    typeof(OpenGameResponse),
    typeof(PingRequest),
    typeof(PingResponse),
    typeof(PlayerEnterRequest),
    typeof(PlayerEnterResponse),
    typeof(PlayerExitRequest),
    typeof(PlayerHealthResponse),
    typeof(PlayerInitCompleteResponse),
    typeof(PlayerInittedResponse),
    typeof(PlayerQuitResponse),
    typeof(PlayerStateRequest),
    typeof(PlayerStatsResponse),
    typeof(RequestCharacterRequest),
    typeof(RequestCharacterResponse),
    typeof(RequestCharacterErrorResponse),
    typeof(RequestWorldLargeRequest),
    typeof(SetTimeResponse),
    typeof(SpeedResponse),
    typeof(WorldEnterResponse),
    typeof(WorldSentResponse))]
public partial class GameServerContext
{
}