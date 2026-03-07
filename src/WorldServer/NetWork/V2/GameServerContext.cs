using Core.Infrastructure.Network;
using WorldServer.NetWork.V2.Dtos;

namespace WorldServer.NetWork.V2;

[PacketSerializerContext(typeof(PlayerEnterRequest))]
public partial class GameServerContext : IPacketSerializerContext
{
}