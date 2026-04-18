namespace Core.Domain.Entities;

public sealed class CharacterBagPool
{
    public int CharacterId { get; set; }
    public int BagType { get; set; }
    public int BagPoolValue { get; set; }
}
