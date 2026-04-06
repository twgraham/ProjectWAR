using Core.GameWorld.Entities;

namespace Core.GameWorld.Components;

/// <summary>
/// Optional component that gives a <see cref="GameObjectEntity"/> a destructible health pool
/// (e.g. keep doors, resource nodes).
/// <para>
/// Unlike <see cref="HealthComponent"/> which is a required direct field on
/// <see cref="UnitEntity"/>, <see cref="DestructibleComponent"/> is an opt-in component
/// attached only to game objects that need health.
/// </para>
/// </summary>
public sealed class DestructibleComponent : IComponent
{
    private WorldEntity? _owner;

    public DestructibleComponent(uint maxHealth, uint doorId = 0)
    {
        ArgumentOutOfRangeException.ThrowIfZero(maxHealth);
        Health = new HealthComponent(maxHealth);
        DoorId = doorId;
    }

    // ── IComponent ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public WorldEntity? Owner => _owner;

    /// <inheritdoc />
    public void OnAttach(WorldEntity entity) => _owner = entity;

    /// <inheritdoc />
    public void OnDetach() => _owner = null;

    // ── Destructible State ───────────────────────────────────────────────

    /// <summary>The health pool for this destructible object.</summary>
    public HealthComponent Health { get; }

    /// <summary>
    /// Protocol door identifier. Written to <c>F_CREATE_STATIC</c> when non-zero.
    /// </summary>
    public uint DoorId { get; }

    /// <summary>Whether this object has been destroyed (HP reached 0).</summary>
    public bool IsDestroyed => Health.IsDead;

    /// <summary>
    /// When <c>true</c>, the object cannot take damage and is excluded from the
    /// attackable flag in <c>F_CREATE_STATIC</c>.
    /// </summary>
    public bool IsInvulnerable { get; set; }
}
