namespace WorldServerV2.World.Entities;

/// <summary>
/// Identifies the concrete type of a <see cref="WorldEntity"/>. Each entity has exactly
/// one type — no combinations. Replaces the legacy runtime <c>is Player</c> / <c>is Creature</c>
/// type checks with a discriminator that mirrors the sealed class hierarchy.
/// </summary>
public enum EntityType : byte
{
    /// <summary>Human-controlled character (<see cref="PlayerEntity"/>).</summary>
    Player,

    /// <summary>NPC mob: standard, champion, hero, lord (<see cref="CreatureEntity"/>).</summary>
    Creature,

    /// <summary>Player-owned creature (<see cref="PetEntity"/>).</summary>
    Pet,

    /// <summary>Static world object: door, chest, capture point (<see cref="GameObjectEntity"/>).</summary>
    GameObject,

    /// <summary>Siege weapon: ram, oil, cannon.</summary>
    Siege,

    /// <summary>Public quest controller.</summary>
    PublicQuest,

    /// <summary>Battlefield objective / keep.</summary>
    Keep,
}
