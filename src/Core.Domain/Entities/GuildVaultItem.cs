namespace Core.Domain.Entities;

public sealed class GuildVaultItem
{
    public uint GuildId { get; set; }
    public uint Entry { get; set; }
    public byte VaultId { get; set; }
    public ushort SlotId { get; set; }
    public ushort Counts { get; set; }
    public string? Talismans { get; set; }
    public ushort PrimaryDye { get; set; }
    public ushort SecondaryDye { get; set; }
}
