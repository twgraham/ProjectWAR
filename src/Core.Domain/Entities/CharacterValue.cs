namespace Core.Domain.Entities;

public sealed class CharacterValue
{
    public uint CharacterId { get; set; }
    public byte Level { get; set; }
    public uint Xp { get; set; }
    public int XpMode { get; set; }
    public uint RestXp { get; set; }
    public uint Renown { get; set; }
    public byte RenownRank { get; set; }
    public uint Money { get; set; }
    public int Speed { get; set; }
    public uint PlayedTime { get; set; }
    public DateTime? LastSeen { get; set; }
    public int RegionId { get; set; }
    public ushort ZoneId { get; set; }
    public int WorldX { get; set; }
    public int WorldY { get; set; }
    public int WorldZ { get; set; }
    public int WorldO { get; set; }
    public ushort RallyPoint { get; set; }
    public byte BagBuy { get; set; }
    public byte BankBuy { get; set; }
    public uint Skills { get; set; }
    public bool Online { get; set; }
    public bool GearShow { get; set; }
    public ushort TitleId { get; set; }
    public string RenownSkills { get; set; } = string.Empty;
    public string MasterySkills { get; set; } = string.Empty;
    public ushort? Morale1 { get; set; }
    public ushort? Morale2 { get; set; }
    public ushort? Morale3 { get; set; }
    public ushort? Morale4 { get; set; }
    public ushort? Tactic1 { get; set; }
    public ushort? Tactic2 { get; set; }
    public ushort? Tactic3 { get; set; }
    public ushort? Tactic4 { get; set; }
    public byte GatheringSkill { get; set; }
    public byte GatheringSkillLevel { get; set; }
    public byte CraftingSkill { get; set; }
    public byte CraftingSkillLevel { get; set; }
    public bool ExperimentalMode { get; set; }
    public uint RVRKills { get; set; }
    public uint RVRDeaths { get; set; }
    public byte CraftingBags { get; set; }
    public uint? PendingXp { get; set; }
    public uint? PendingRenown { get; set; }
    public string Lockouts { get; set; } = string.Empty;
    public DateTime DisconcetTime { get; set; }
    
    public Character Character { get; set; } = null!;
}
