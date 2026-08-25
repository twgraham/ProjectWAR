namespace Core.Domain.Entities;

/// <summary>
/// A per-entry stat override from the <c>creature_stats</c> table.
/// Negative <see cref="StatValue"/> subtracts from the item-bonus layer;
/// positive values add to it — matching V1 <c>Creature_stats</c> semantics.
/// </summary>
public sealed class CreatureStatEntry
{
    public uint ProtoEntry { get; set; }
    public uint StatId     { get; set; }
    public int  StatValue  { get; set; }
}
