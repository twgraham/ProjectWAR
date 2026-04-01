using System.Collections.Immutable;
using WorldServerV2.Data.Entities;
using WorldServerV2.Network;
using WorldServerV2.Network.Dtos;
using WorldServerV2.World.Entities;

namespace WorldServerV2.World.Components;

/// <summary>
/// Visual equipment attached to a creature. Sends <c>F_PLAYER_INVENTORY</c>
/// when the creature becomes visible to a player.
/// </summary>
/// <remarks>
/// <b>Tech debt</b>: Primary/secondary color fields in <see cref="CreatureItem"/> are
/// loaded but not yet serialized — see <see cref="EquippedInventoryResponse"/> remarks.
/// </remarks>
public sealed class EquipmentComponent(ImmutableArray<CreatureItem> items) : ComponentBase, IVisibilityInitContributor
{
    /// <summary>The equipped item slots for this creature.</summary>
    public ImmutableArray<CreatureItem> Items { get; } = items;

    /// <inheritdoc />
    public void SendVisibilityInit(GameSession session)
    {
        if (Owner is null)
            return;

        session.SendEquippedInventory(EquippedInventoryResponse.From(Owner, Items));
    }
}
