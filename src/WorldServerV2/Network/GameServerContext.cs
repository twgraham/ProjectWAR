using Core.Infrastructure.Network;
using WorldServerV2.Network.Dtos;

namespace WorldServerV2.Network;

[PacketSerializerContext(typeof(PlayerEnterRequest))]
public partial class GameServerContext : IPacketSerializerContext
{
}