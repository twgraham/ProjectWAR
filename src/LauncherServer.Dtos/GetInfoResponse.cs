using Core.Infrastructure.Network;
using Core.Infrastructure.Network.Serialization.Attributes;

namespace LauncherServer.Dtos;

public class GetInfoResponse
{
    [PacketLength(1)]
    public List<RealmInfo> RealmInfo { get; set; }
}