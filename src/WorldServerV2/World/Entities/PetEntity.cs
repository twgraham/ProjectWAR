using WorldServerV2.Data.Entities;

namespace WorldServerV2.World.Entities;

/// <summary>
/// A player-owned creature (pet). Shares creature data (<see cref="Proto"/>,
/// <see cref="Spawn"/>) but also tracks the owning unit.
/// </summary>
public sealed class PetEntity : UnitEntity
{
    public PetEntity(ushort objectId, CreatureProto proto, CreatureSpawn spawn, UnitEntity owner, uint maxHealth)
        : base(objectId, EntityType.Pet, proto.Name ?? string.Empty, maxHealth)
    {
        Proto = proto ?? throw new ArgumentNullException(nameof(proto));
        Spawn = spawn ?? throw new ArgumentNullException(nameof(spawn));
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>The creature template.</summary>
    public CreatureProto Proto { get; }

    /// <summary>The spawn record.</summary>
    public CreatureSpawn Spawn { get; }

    /// <summary>The unit that owns this pet.</summary>
    public UnitEntity Owner { get; }

    /// <summary>Shorthand for the prototype's entry ID.</summary>
    public uint Entry => Proto.Entry;
}
