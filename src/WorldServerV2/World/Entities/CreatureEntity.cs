using WorldServerV2.Data.Entities;

namespace WorldServerV2.World.Entities;

/// <summary>
/// An NPC mob in the game world (standard, champion, hero, lord).
/// Holds the prototype template as a direct field; per-spawn state (level, faction, emote,
/// model, scale) is set by <see cref="WorldServerV2.World.Spawning.IEntityFactory"/> after
/// construction.
/// </summary>
public sealed class CreatureEntity : UnitEntity
{
    public CreatureEntity(ushort objectId, CreatureProto proto, uint maxHealth)
        : base(objectId, EntityType.Creature, proto.Name ?? string.Empty, maxHealth)
    {
        Proto = proto ?? throw new ArgumentNullException(nameof(proto));
    }

    /// <summary>The creature template (stats, model, faction, etc.).</summary>
    public CreatureProto Proto { get; }

    /// <summary>Shorthand for the prototype's entry ID.</summary>
    public uint Entry => Proto.Entry;

    /// <summary>
    /// Model ID resolved at factory time from <c>Proto.Model1/Model2</c>.
    /// Stored here so the DTO mapping can read it without re-randomising on each visibility send.
    /// </summary>
    public ushort ModelId { get; set; }

    /// <summary>
    /// Visual scale resolved at factory time from <c>Proto.MinScale/MaxScale</c>.
    /// </summary>
    public ushort Scale { get; set; }

    /// <summary>
    /// Emote animation resolved at factory time from the spawn descriptor override or
    /// <c>Proto.Emote</c>.
    /// </summary>
    public byte Emote { get; set; }
}

