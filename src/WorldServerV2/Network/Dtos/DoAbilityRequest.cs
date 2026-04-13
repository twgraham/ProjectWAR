namespace WorldServerV2.Network.Dtos;

public class DoAbilityRequest
{
    public byte LineOfSight { get; set; }
    
    public bool IsMoving { get; set; }
    
    public ushort Heading { get; set; }
    public ushort PositionX { get; set; }
    public ushort PositionY { get; set; }
    public ushort ZoneId { get; set; }
    public ushort PositionZ { get; set; }
    public ushort AbilityId { get; set; }
    public byte AbilityGroup { get; set; }
    public byte Unk1 { get; set; }
    public ushort Unk2 { get; set; }
    
    public bool IsEnemyVisible() => Convert.ToBoolean(LineOfSight & 128);
    public bool IsFriendlyVisible() => Convert.ToBoolean(LineOfSight & 8);
}