namespace WorldServerV2.Data.Models;

public class NewCharacter
{
    public byte Slot { get; set; }
    public string Name { get; set; }
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
    }
}