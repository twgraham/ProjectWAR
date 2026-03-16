namespace WorldServerV2.Data.Entities;

/// <summary>
/// A banned or filtered character name, mapped to the <c>banned_names</c> table.
/// Pure POCO — all DB column mapping is handled by <see cref="CharacterDbContext"/>.
/// </summary>
public sealed class BannedName
{
    public string NameString { get; set; } = string.Empty;
    public string? FilterTypeString { get; set; }
}
