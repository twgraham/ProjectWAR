using Core.GameWorld.Entities;
using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

public class CreatePlayerResponse
{
    public ushort SessionId { get; set; }
    public ushort ObjectId { get; set; }
    public ushort ModelId { get; set; }
    public ushort CareerLine { get; set; }
    public ushort Z { get; set; }
    public ushort ZoneId { get; set; }
    public ushort X { get; set; }
    public ushort Y { get; set; }
    public ushort Heading { get; set; }
    public byte Level { get; set; }
    public byte EffectiveLevel { get; set; }
    public bool ShowHerald { get; set; }
    public byte Faction { get; set; }
    public ushort TitleId { get; set; }
    
    [FixedLength(8)]
    public byte[] Traits { get; set; } = new byte[8];
    
    public ushort EnemyTargetId { get; set; }
    public ushort AllyTargetId { get; set; }
    
    public byte Race { get; set; }
    public byte Sex { get; set; }
    public byte RenownRank { get; set; }
    public byte PctHealth { get; set; }
    
    [FixedLength(8)]
    public byte[] Padding { get; set; } = new byte[8];
    
    [PascalString]
    public string FirstName { get; set; }
    [PascalString]
    public string LastName { get; set; }
    [PascalString]
    public string GuildName { get; set; }

    [FixedLength(4)]
    public byte[] Padding2 { get; set; } = new byte[4];

    public static CreatePlayerResponse From(ushort sessionId, PlayerEntity player)
    {
        return new CreatePlayerResponse
        {
            SessionId = sessionId,
            ObjectId = player.ObjectId,
            ModelId = player.Character.ModelId,
            CareerLine = player.Character.CareerLine,
            Z = (ushort)player.Position.Z,
            ZoneId = player.Position.ZoneId,
            X = (ushort)player.Position.X,
            Y = (ushort)player.Position.Y,
            Heading = player.Position.Heading,
            Level = player.Level,
            EffectiveLevel = player.Level,
            ShowHerald = false, // TODO: fix flags here
            Faction = (byte)(player.Faction + (player.Health.IsDead ? 1 : 0)), // TODO: adjust based on dead/alive state
            TitleId = 0, // TODO: add title id here
            Traits = player.Character.Traits,
            EnemyTargetId = 0, // TODO: set current enemy target id here
            AllyTargetId = 0, // TODO: set current ally target id here
            Race = player.Character.Race,
            Sex = player.Character.Sex,
            RenownRank = 0, // TODO: set renown rank here
            PctHealth = player.Health.Percent,
            FirstName = player.Character.Name,
            LastName = player.Character.Surname,
            GuildName = "" // TODO: set guild name here
        };
    }
}