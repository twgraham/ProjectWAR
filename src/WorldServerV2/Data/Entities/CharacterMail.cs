namespace WorldServerV2.Data.Entities;

/// <summary>
/// An in-game mail message, mapped to the <c>characters_mails</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class CharacterMail
{
    /// <summary>Auto-generated identity PK.</summary>
    public int Guid { get; set; }
    public byte AuctionType { get; set; }
    public uint CharacterId { get; set; }
    public uint CharacterIdSender { get; set; }
    public string SenderName { get; set; } = string.Empty;
    public string ReceiverName { get; set; } = string.Empty;
    public DateTime SendDate { get; set; }
    public DateTime ReadDate { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public uint Money { get; set; }
    public bool Cr { get; set; }
    public bool Opened { get; set; }
    public string ItemsString { get; set; } = string.Empty;
}
