using WorldServerV2.Data.Entities;

namespace WorldServerV2.World.Entities;

/// <summary>
/// A human-controlled character in the game world. Holds the persistent
/// <see cref="Character"/> record and session-scoped state as direct fields.
/// <para>
/// <b>Type safety</b>: APIs that require a player (e.g. <c>PlayerService.Bind</c>)
/// accept <c>PlayerEntity</c> — not <c>WorldEntity</c> — making it impossible at
/// compile time to pass a creature or game object.
/// </para>
/// </summary>
public sealed class PlayerEntity : UnitEntity
{
    public PlayerEntity(ushort objectId, Character character, uint maxHealth)
        : base(objectId, EntityType.Player,
            (character ?? throw new ArgumentNullException(nameof(character))).Name, maxHealth)
    {
        Character = character;
    }

    /// <summary>The persistent DB character record.</summary>
    public Character Character { get; }

    /// <summary>Shorthand for <see cref="Character.CharacterId"/>.</summary>
    public uint CharacterId => Character.CharacterId;

    /// <summary>How the player disconnected (set during the logout flow).</summary>
    public DisconnectType DisconnectType { get; set; }

    /// <summary>
    /// Initializes the entity's runtime state from the persistent <see cref="Character"/>
    /// record. Sets level, realm, faction, and restores health.
    /// <para>
    /// Call this once on the handler thread after OID assignment and before init steps
    /// send packets. This keeps entity state initialization cohesive with the entity
    /// rather than scattered across external services.
    /// </para>
    /// </summary>
    public void InitializeFromCharacter()
    {
        var character = Character;
        Level = character.Value.Level;
        Realm = character.Realm;
        // Faction mirrors realm for players (1 = Order, 2 = Destruction).
        Faction = character.Realm;

        // Health: set max then heal to full.
        // In V1 the stats system computes max HP from Wounds + level + bonuses.
        // For now we use the default max health the entity was constructed with.
        Health.Resurrect(100);
    }
}
