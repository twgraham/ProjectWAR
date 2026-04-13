using Core.GameWorld.Components;
using Core.GameWorld.DataStore.Models;
using Core.GameWorld.Entities;

namespace Core.GameWorld.Spawning;

/// <summary>
/// Creates runtime <see cref="WorldEntity"/> instances from spawn descriptors.
/// <para>
/// Entities are returned with <see cref="WorldEntity.ObjectId"/> set to 0.
/// The region thread assigns a real OID via <see cref="WorldEntity.AssignOid"/> when
/// the entity is placed via <c>Region.EnqueueAdd</c>.
/// </para>
/// </summary>
public interface IEntityFactory
{
    /// <summary>
    /// Creates a <see cref="CreatureEntity"/> from the supplied descriptor.
    /// Attaches optional components (movement, AI, vendor, etc.) based on prototype flags.
    /// </summary>
    /// <param name="descriptor">Source data for this spawn.</param>
    CreatureEntity CreateCreature(SpawnDescriptor descriptor);

    /// <summary>
    /// Creates a <see cref="GameObjectEntity"/> from the supplied descriptor.
    /// Attaches a <see cref="DestructibleComponent"/> when the prototype
    /// has non-zero health.
    /// </summary>
    /// <param name="descriptor">Source data for this spawn.</param>
    GameObjectEntity CreateGameObject(GameObjectSpawnDescriptor descriptor);
}
