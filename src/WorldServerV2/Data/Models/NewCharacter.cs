using Core.Domain.Entities;
using Core.Domain.ValueObjects;
using Core.GameWorld.DataStore.Models;

namespace WorldServerV2.Data.Models;

public class NewCharacter
{
    public byte Slot { get; set; }
    public string Name { get; set; } = string.Empty;
    public Race Race { get; set; }
    public Sex Sex { get; set; }
    public Class Class { get; set; }
    public byte Model { get; set; }

    public byte[] Traits
    {
        get;
        set
        {
            if (value.Length != 8)
                throw new ArgumentException("Traits array must be exactly 8 bytes long.");
            field = value;
        }
    } = new byte[8];

    public Character ToEntity(uint accountId, int realmId, ClassData classData)
    {
        var classInfo = classData.Infos[Class];
        var classItems = classData.Items[Class];

        return new Character
        {
            AccountId = (int)accountId,
            SlotId = Slot,
            Name = Name,
            Race = (byte)Race,
            Sex = (byte)Sex,
            Career = (byte)Class,
            Traits = Traits,
            ModelId = Model,
            Realm = (byte)Race.GetFaction(),
            RealmId = realmId,
            FirstConnect = true,
            Value = new CharacterValue
            {
                Level = 1,
                Money = 2000,
                Online = false,
                RallyPoint = classInfo.RallyPt,
                RegionId = classInfo.Region,
                ZoneId = classInfo.ZoneId,
                Renown = 0,
                RenownRank = 1,
                RestXp = 0,
                Skills = classInfo.Skills,
                Speed = 100,
                PlayedTime = 0,
                WorldX = classInfo.WorldX,
                WorldY = classInfo.WorldY,
                WorldZ = classInfo.WorldZ,
                WorldO = classInfo.WorldO
            },
            Items = classItems.Select(x => new CharacterItem
            {
                Entry = x.Entry,
                SlotId = x.SlotId,
                ModelId = x.ModelId,
                Counts = x.Count
            }).ToList()
        };
    }
}