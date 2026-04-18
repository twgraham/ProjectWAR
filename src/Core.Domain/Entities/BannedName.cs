namespace Core.Domain.Entities;

public sealed class BannedName
{
    public string NameString { get; set; } = string.Empty;
    public string? FilterTypeString { get; set; }
}
