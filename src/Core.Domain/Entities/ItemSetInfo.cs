namespace Core.Domain.Entities;

public sealed class ItemSetInfo
{
    public uint Entry { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Buff level modifier applied when granting set spell bonuses.</summary>
    public byte Unk { get; set; }

    /// <summary>
    /// Pipe-delimited item list: <c>"itemId:itemName|itemId:itemName|..."</c>.
    /// </summary>
    public string ItemsString { get; set; } = string.Empty;

    /// <summary>
    /// Pipe-delimited bonus list: <c>"key:values|key:values|..."</c>.
    /// Key &lt; 80 → stat bonus (<c>"key:statId,value,percentage"</c>).
    /// Key &gt;= 80 → spell bonus (<c>"key:spellId"</c>).
    /// </summary>
    public string BonusString { get; set; } = string.Empty;
}
