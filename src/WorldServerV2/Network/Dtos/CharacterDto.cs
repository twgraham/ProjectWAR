using Core.Infrastructure.Network;

namespace WorldServerV2.Network.Dtos;

public class CharacterDto
{
    [CString(24)]
    public required string Name { get; set; }
    
    [CString(24)]
    public required string Surname { get; set; }
    
    public byte Level { get; set; }
    
    public byte Career { get; set; }
    
    public byte Realm { get; set; }
    public byte Sex { get; set; }
    public byte ModelId { get; set; }
    public byte Unk1 { get; set; }
    
    [LittleEndian]
    public ushort ZoneId { get; set; }

    public uint Unk2 { get; set; } = 0;
    
    public CharacterItemDto[] Items { get; set; } = new CharacterItemDto[18];
    
    public byte[] Padding { get; set; } = [
        0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00
    ];
    
    [LittleEndian]
    public uint Slot1 { get; set; }
    [LittleEndian]
    public uint Slot2 { get; set; }
    [LittleEndian]
    public uint Slot3 { get; set; }

    public byte[] Padding2 { get; set; } =
    [
    ];
    
    public byte Race { get; set; }
    public ushort TitleId { get; set; }
    public required byte[] Traits { get; set; }
    public byte[] Padding3 { get; set; } =
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    ];
}