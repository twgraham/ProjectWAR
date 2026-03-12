using Core.Infrastructure.Network;

namespace WorldServerV2.Network.Dtos;

public class ConnectResponse
{
    public uint Unk1 { get; set; } = 0;
    public uint Version { get; set; }
    public byte RealmId { get; set; }
    public byte Unk2 { get; set; } = 0;
    public byte Unk3 { get; set; } = 0;
    public byte Unk4 { get; set; } = 0;
    public bool TransferFlag { get; set; }
    [PascalString]
    public required string Username { get; set; }
    [PascalString]
    public required string RealmName { get; set; }
    public byte Unk5 { get; set; } = 0;
    public ushort Unk6 { get; set; } = 0;
}