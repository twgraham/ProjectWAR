using Core.Domain.Entities;
using Core.Infrastructure.Network;
using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

public class CharacterDto
{
    [CString(24)]
    public string Name { get; set; } = string.Empty;
    
    [CString(24)]
    public string Surname { get; set; } = string.Empty;
    
    public byte Level { get; set; }
    
    public byte Career { get; set; }
    
    public byte Realm { get; set; }
    public byte Sex { get; set; }
    public byte ModelId { get; set; }
    public byte Unk1 { get; } = 0;
    
    [LittleEndian]
    public ushort ZoneId { get; set; }

    public uint Unk2 { get; set; }
    
    [FixedLength(18)]
    public CharacterItemDto[] Items { get; set; } = new CharacterItemDto[18];
    
    [FixedLength(32)]
    public byte[] Padding { get; set; } = [
        0x00, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00,
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

    [FixedLength(11)]
    public byte[] Padding2 { get; } =
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x01, 0x00, 0x00
    ];
    
    public byte Race { get; set; }
    public ushort TitleId { get; set; }
    [FixedLength(8)]
    public byte[] Traits { get; set; } = new byte[8];
    [FixedLength(14)]
    public byte[] Padding3 { get; } =
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00
    ];

    public CharacterDto()
    {
        Items = Enumerable.Range(0, 18).Select(x => new CharacterItemDto()).ToArray();
    }

    public CharacterDto(Character character)
    {
        Name = character.Name;
        Surname = character.Surname;
        Level = character.Level;
        Career = character.Career;
        Realm = character.Realm;
        Sex = character.Sex;
        ModelId = character.ModelId;
        Race = character.Race;
        Traits = character.Traits;
        ZoneId = character.Value.ZoneId;
        Items = new CharacterItemDto[18];

        var slotItems = character.Items.ToDictionary(x => x.SlotId, x => x);

        for (ushort slotId = 10; slotId < 13; slotId++)
        {
            slotItems.TryGetValue(slotId, out var item);

            switch (slotId)
            {
                case 10:
                    Slot1 = item?.ModelId ?? 0;
                    break;
                case 11:
                    Slot2 = item?.ModelId ?? 0;
                    break;
                case 12:
                    Slot3 = item?.ModelId ?? 0;
                    break;
            }
        }
        
        for (ushort slotId = 19; slotId < 37; slotId++)
        {
            if (!slotItems.TryGetValue(slotId, out var item))
                Items[slotId - 19] = new CharacterItemDto();
            else
                Items[slotId - 19] = new CharacterItemDto
                {
                    ModelId = item.ModelId,
                    PrimaryDye = item.PrimaryDye,
                    SecondaryDye = item.SecondaryDye
                };
        }
    }
}