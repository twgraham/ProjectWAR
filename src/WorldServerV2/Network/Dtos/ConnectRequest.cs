using System.ComponentModel.DataAnnotations;
using Core.Infrastructure.Network;

namespace WorldServerV2.Network.Dtos;

public class ConnectRequest
{
    public byte Unk1 { get; set; }
    
    public byte Unk2 { get; set; }
    
    public byte MajorVersion { get; set; }
    
    public byte MinorVersion { get; set; }
    
    public byte PatchVersion { get; set; }
    
    [Length(3, 3)]
    public byte[] Padding { get; set; } = new byte[3];
    
    public uint ProtocolVersion { get; set; }
    
    [CString(81)]
    public string Token { get; set; }
    
    [Length(20, 20)]
    public byte[] Unk3 { get; set; } = new byte[16];
    
    [CString(23)]
    public string Username { get; set; }
}