using Core.GameWorld.DataStore.Models;

namespace Core.GameWorld.Entities;

/// <summary>
/// A static world object (door, chest, capture point, quest object, etc.).
/// Not a <see cref="UnitEntity"/> — game objects don't have health in most cases.
/// If a specific game object needs destructible health (e.g., keep doors), it carries
/// a <see cref="Components.DestructibleComponent"/> as an optional component.
/// </summary>
public sealed class GameObjectEntity : WorldEntity
{
    public GameObjectEntity(
        ushort objectId,
        GameObjectSpawnDescriptor descriptor,
        string? nameOverride = null)
        : base(objectId, EntityType.GameObject, nameOverride ?? $"GO_{descriptor.Entry}")
    {
        Descriptor   = descriptor;
        VfxState     = descriptor.VfxState;
        Interactable = descriptor.Interactable;
    }

    /// <summary>
    /// The spawn descriptor that sourced this entity — carries the raw DB fields
    /// needed for the wire protocol (Unks, DisplayId, DoorId, SpawnUnk1-4).
    /// </summary>
    public GameObjectSpawnDescriptor Descriptor { get; }

    /// <summary>Template entry ID from the game data store.</summary>
    public uint Entry => Descriptor.Entry;

    /// <summary>Visual effect state (door open/closed, glow, etc.).</summary>
    public byte VfxState { get; set; }

    /// <summary>Whether this object can be interacted with.</summary>
    public bool Interactable { get; set; }
}
