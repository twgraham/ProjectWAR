using WorldServerV2.Data.Entities;

namespace WorldServerV2.World.Entities;

/// <summary>
/// An NPC mob in the game world (standard, champion, hero, lord).
/// Holds the prototype template and the specific spawn-point record as direct fields.
/// </summary>
public sealed class CreatureEntity : UnitEntity
{
    public CreatureEntity(ushort objectId, CreatureProto proto, CreatureSpawn spawn, uint maxHealth)
        : base(objectId, EntityType.Creature, proto.Name ?? string.Empty, maxHealth)
    {
        Proto = proto ?? throw new ArgumentNullException(nameof(proto));
        Spawn = spawn ?? throw new ArgumentNullException(nameof(spawn));
    }

    /// <summary>The creature template (stats, model, faction, etc.).</summary>
    public CreatureProto Proto { get; }

    /// <summary>The spawn point that placed this creature in the world.</summary>
    public CreatureSpawn Spawn { get; }

    /// <summary>Shorthand for the prototype's entry ID.</summary>
    public uint Entry => Proto.Entry;
}
