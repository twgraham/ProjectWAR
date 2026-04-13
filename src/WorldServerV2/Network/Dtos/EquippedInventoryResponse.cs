using System.Collections.Immutable;
using Core.Domain.Entities;
using Core.GameWorld.Entities;
using Core.Infrastructure.Network.Serialization.Attributes;

namespace WorldServerV2.Network.Dtos;

/// <summary>
/// <c>F_PLAYER_INVENTORY</c> (0xBD) — NPC/creature equipped-items variant.
/// <para>
/// Sent immediately after <c>F_CREATE_MONSTER</c> for creatures that have visual equipment
/// defined in the <c>creature_items</c> table. This is the standalone packet form — the
/// 5-byte stub embedded in <see cref="CreateMonsterResponse"/> is just a placeholder.
/// </para>
/// </summary>
/// <remarks>
/// <para><b>Byte layout</b>:</para>
/// <code>
/// Oid(u16)  WeaponStance(u8)  ItemCount(u8) — via [PacketLength(1)]
///   per slot: Flags(u8)  SlotId(u8)  ModelId(u16)  [if Flags==1: EffectId(u16)]
/// Terminator(u8)=0
/// </code>
/// <para><b>Tech debt</b>: Primary/secondary color support is deferred. The old server
/// encodes colors with variable-width fields gated by bitwise flag checks
/// (<c>ColorOverride</c>, <c>PriColorExpansion</c>, <c>SecColorExpansion</c>), but the source
/// generator's <c>[ConditionalOn]</c> only supports equality tests. Since the vast majority
/// of creature items have both colors set to 0, the effect-only path covers current data.
/// When color support is needed, either extend <c>[ConditionalOn]</c> for bitwise tests or
/// use a <c>[RawBytes]</c> manual serialization block.</para>
/// </remarks>
public sealed class EquippedInventoryResponse
{
    public ushort Oid { get; set; }

    /// <summary>
    /// Weapon stance — always <c>0</c> (Melee) for NPCs.
    /// Players would use their current <c>WeaponStance</c> value.
    /// </summary>
    public byte WeaponStance { get; set; }

    /// <summary>
    /// Equipped item slots. The list count is written as a 1-byte length prefix.
    /// </summary>
    [PacketLength(1)]
    public List<EquippedSlot> Slots { get; set; } = [];

    /// <summary>Terminator byte — always 0. Client uses this to detect end-of-inventory.</summary>
    public byte Terminator { get; set; }

    /// <summary>
    /// Builds an <see cref="EquippedInventoryResponse"/> from a creature's equipment data.
    /// </summary>
    /// <param name="entity">The creature entity (Oid must be assigned).</param>
    /// <param name="items">The creature's equipped item definitions.</param>
    public static EquippedInventoryResponse From(
        WorldEntity entity,
        ICollection<CreatureItem> items)
    {
        var slots = new List<EquippedSlot>(items.Count);
        foreach (var item in items)
        {
            slots.Add(new EquippedSlot
            {
                Flags = item.EffectId > 0 ? (byte)1 : (byte)0,
                SlotId = (byte)item.SlotId,
                ModelId = item.ModelId,
                EffectId = (ushort)item.EffectId,
            });
        }

        return new EquippedInventoryResponse
        {
            Oid = entity.ObjectId,
            WeaponStance = 0,
            Slots = slots,
        };
    }
    
    public static EquippedInventoryResponse From(PlayerEntity entity)
    {
        var items = entity.Inventory.GetEquippedItems().ToList();
        var slots = new List<EquippedSlot>(items.Count);
        foreach (var item in items)
        {
            slots.Add(new EquippedSlot
            {
                Flags = 0, // item.EffectId > 0 ? (byte)1 : (byte)0,
                SlotId = (byte)item.SlotId,
                ModelId = (ushort)item.ModelId,
            });
        }

        return new EquippedInventoryResponse
        {
            Oid = entity.ObjectId,
            WeaponStance = 0,
            Slots = slots
        };
    }
}

/// <summary>
/// A single equipped item slot within an <see cref="EquippedInventoryResponse"/>.
/// </summary>
public sealed class EquippedSlot
{
    /// <summary>
    /// Item flags byte: <c>0</c> = model only, <c>1</c> = model + effect.
    /// <para>
    /// <b>Tech debt</b>: The full flag set includes <c>ColorOverride(0x01)</c>,
    /// <c>Trophy(0x03)</c>, <c>Heraldry(0x04)</c>, <c>AltAppearance(0x20)</c>,
    /// <c>SecColorExpansion(0x40)</c>, <c>PriColorExpansion(0x80)</c>.
    /// Only the effect-only NPC path (0/1) is implemented.
    /// </para>
    /// </summary>
    public byte Flags { get; set; }

    public byte SlotId { get; set; }
    public ushort ModelId { get; set; }

    /// <summary>
    /// Visual effect / enchant glow ID. Only serialized when <see cref="Flags"/> == 1.
    /// </summary>
    [ConditionalOn(nameof(Flags), 1)]
    public ushort EffectId { get; set; }
}
