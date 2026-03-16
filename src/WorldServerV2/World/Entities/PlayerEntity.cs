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
}
