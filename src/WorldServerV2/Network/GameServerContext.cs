using Core.Infrastructure.Network;
using Core.Infrastructure.Network.Serialization;
using WorldServerV2.Network.Dtos;

namespace WorldServerV2.Network;

// Source-generated fast-path serializers for all binary DTOs.
[PacketSerializerContext(
    typeof(AbilityListResponse),
    typeof(AccountCharacterModifiedResponse),
    typeof(AccountCharacterModifyErrorResponse),
    typeof(BagInfoResponse),
    typeof(CareerAbilityResponse),
    typeof(CareerCategoryResponse),
    typeof(CareerPackageUpdateResponse),
    typeof(CharacterTemplatesRequest),
    typeof(CharacterTemplatesResponse),
    typeof(CheckNameResponse),
    typeof(ConnectRequest),
    typeof(ConnectResponse),
    typeof(CreateCharacterRequest),
    typeof(CreateMonsterResponse),
    typeof(CreateStaticResponse),
    typeof(DeleteCharacterRequest),
    typeof(DeleteNameRequest),
    typeof(DoAbilityRequest),
    typeof(DumpArenasLargeRequest),
    typeof(EncryptKeyRequest),
    typeof(EncryptKeyResponse),
    typeof(GetItemResponse),
    typeof(InitializePlayerRequest),
    typeof(InterfaceCommandRequest),
    typeof(MasterySkillResponse),
    typeof(MasteryTreePointsResponse),
    typeof(MoraleListResponse),
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
    typeof(PlayerStateRelayResponse),
    typeof(PlayerStateRequest),
    typeof(PlayerStatsResponse),
    typeof(RequestCharacterRequest),
    typeof(RequestCharacterResponse),
    typeof(RequestCharacterErrorResponse),
    typeof(RequestWorldLargeRequest),
    typeof(SetTimeResponse),
    typeof(SkillListResponse),
    typeof(SpeedResponse),
    typeof(TacticsResponse),
    typeof(WorldEnterResponse),
    typeof(WorldSentResponse))]
public partial class GameServerContext
{
}