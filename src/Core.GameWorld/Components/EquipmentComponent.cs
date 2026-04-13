using System.Collections.Immutable;
using Core.Domain.Entities;

namespace Core.GameWorld.Components;

/// <summary>
/// Visual equipment attached to a creature. Sends <c>F_PLAYER_INVENTORY</c>
/// when the creature becomes visible to a player.
/// </summary>
/// <remarks>
/// <b>Tech debt</b>: Primary/secondary color fields in <see cref="CreatureItem"/> are
/// loaded but not yet serialized — see <see cref="EquippedInventoryResponse"/> remarks.
/// </remarks>
public sealed class EquipmentComponent(ImmutableArray<CreatureItem> items) : ComponentBase
{
    /// <summary>The equipped item slots for this creature.</summary>
    public ImmutableArray<CreatureItem> Items { get; } = items;
}
