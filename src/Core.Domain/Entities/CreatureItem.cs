namespace Core.Domain.Entities;

/// <summary>
/// A single visual equipment slot for a creature prototype, loaded from the
/// <c>creature_items</c> table. Keyed by <c>(Entry, SlotId)</c>.
/// </summary>
/// <remarks>
/// <b>Tech debt</b>: <see cref="PrimaryColor"/> and <see cref="SecondaryColor"/> are stored
/// but not yet serialized in the <c>F_PLAYER_INVENTORY</c> DTO. The client supports a
/// variable-width color encoding (flags &amp; <c>ColorOverride</c>, <c>PriColorExpansion</c>,
/// <c>SecColorExpansion</c>) that requires bitwise flag checks which the source generator's
/// <c>[ConditionalOn]</c> attribute cannot express (equality-only). Most NPC data rows have
/// both colors set to 0, so the effect-only path is sufficient for now. When colors are
/// needed, either extend <c>[ConditionalOn]</c> to support bitwise tests or use a
/// <c>[RawBytes]</c> manual block.
/// </remarks>
public sealed class CreatureItem
{
    /// <summary>Creature prototype entry ID (foreign key to <c>creature_protos.entry</c>).</summary>
    public uint Entry { get; set; }

    /// <summary>Equipment slot number (e.g. 10 = main hand, 20 = body).</summary>
    public ushort SlotId { get; set; }

    /// <summary>Visual model ID rendered on the creature.</summary>
    public ushort ModelId { get; set; }

    /// <summary>Visual effect / enchant glow. Written as <c>u16</c> in the packet when non-zero.</summary>
    public uint EffectId { get; set; }

    /// <summary>Primary dye color override. <b>Not yet serialized</b> — see class remarks.</summary>
    public ushort PrimaryColor { get; set; }

    /// <summary>Secondary dye color override. <b>Not yet serialized</b> — see class remarks.</summary>
    public ushort SecondaryColor { get; set; }
}
