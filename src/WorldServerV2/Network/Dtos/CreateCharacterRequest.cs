using Core.Infrastructure.Network.Serialization.Attributes;
using WorldServerV2.Data.Models;

namespace WorldServerV2.Network.Dtos;

public class CreateCharacterRequest
{
    public byte Slot { get; set; }
    public Race Race { get; set; }
    public Class Class { get; set; }
    public Sex Sex { get; set; }
    public byte Model { get; set; }
    public ushort NameSize { get; set; }
    public ushort Padding1 { get; set; }
    
    [FixedLength(8)]
    public byte[] Traits { get; set; } = new byte[8];
    
    [FixedLength(7)]
    public byte[] Padding2 { get; set; }
    
    [CString]
    public string Name { get; set; }

    public NewCharacter ToNewCharacterModel()
    {
        return new NewCharacter
        {
            Class = Class,
            Race = Race,
            Sex = Sex,
            Traits = Traits,
            Name = Name,
            Slot = Slot,
            Model = Model
        };
    }
}
