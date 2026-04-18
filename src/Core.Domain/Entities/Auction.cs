namespace Core.Domain.Entities;

public sealed class Auction
{
    public long AuctionId { get; set; }
    public byte Realm { get; set; }
    public uint SellerId { get; set; }
    public uint ItemId { get; set; }
    public uint SellPrice { get; set; }
    public ushort Count { get; set; }
    public DateTime StartTime { get; set; }
    public string? Talismans { get; set; }
    public ushort PrimaryDye { get; set; }
    public ushort SecondaryDye { get; set; }
}
