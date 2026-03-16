namespace WorldServerV2.Data.Entities;

/// <summary>
/// An item stored in a guild vault, mapped to the <c>guild_vault_item</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
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
