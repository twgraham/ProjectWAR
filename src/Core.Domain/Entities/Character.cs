namespace Core.Domain.Entities;

public sealed class Character
{
    public uint CharacterId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Surname { get; set; } = string.Empty;
    public int RealmId { get; set; }
    public int AccountId { get; set; }
    public byte SlotId { get; set; }
    public byte ModelId { get; set; }
    public byte Career { get; set; }
    public byte CareerLine { get; set; }
    public byte Realm { get; set; }
    public int HeldLeft { get; set; }
    public byte Race { get; set; }
    public byte[] Traits{ get; set; } = [];
    public byte Sex { get; set; }
    public bool Anonymous { get; set; }
    public bool Hidden { get; set; }
    public string OldName { get; set; } = string.Empty;
    public string PetName { get; set; } = string.Empty;
    public ushort PetModel { get; set; }
    public ushort HonorPoints { get; set; }
    public ushort HonorRank { get; set; }

    /// <summary>Computed career flag bitmask.</summary>
    public uint CareerFlags => CareerLine != 0 ? 1u << (CareerLine - 1) : 0;

    public byte Level { get; set; } = 1;
    public bool FirstConnect { get; set; }
    
    public CharacterValue Value { get; set; }
    public List<CharacterItem> Items { get; set; }
}
