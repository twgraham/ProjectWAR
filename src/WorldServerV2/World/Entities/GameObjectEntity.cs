namespace WorldServerV2.World.Entities;

/// <summary>
/// A static world object (door, chest, capture point, quest object, etc.).
/// Not a <see cref="UnitEntity"/> — game objects don't have health in most cases.
/// If a specific game object needs destructible health (e.g., keep doors), it can
/// hold a <see cref="Components.HealthComponent"/> as an optional component.
/// </summary>
public sealed class GameObjectEntity : WorldEntity
{
    public GameObjectEntity(ushort objectId, uint entry, string name)
        : base(objectId, EntityType.GameObject, name)
    {
        Entry = entry;
    }

    /// <summary>Template entry ID from the game data store.</summary>
    public uint Entry { get; }

    /// <summary>Visual effect state (door open/closed, glow, etc.).</summary>
    public byte VfxState { get; set; }

    /// <summary>Whether this object can be interacted with.</summary>
    public bool Interactable { get; set; } = true;
}
