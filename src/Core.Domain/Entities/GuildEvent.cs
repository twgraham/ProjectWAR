namespace Core.Domain.Entities;

public sealed class GuildEvent
{
    /// <summary>Surrogate UUID primary key.</summary>
    public Guid GuildEventId { get; set; }
    public byte SlotId { get; set; }
    public uint GuildId { get; set; }
    public uint CharacterId { get; set; }
    public DateTime Begin { get; set; }
    public DateTime End { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool Alliance { get; set; }
    public bool Locked { get; set; }
    /// <summary>Serialised signup list.</summary>
    public string Signups { get; set; } = string.Empty;
}
