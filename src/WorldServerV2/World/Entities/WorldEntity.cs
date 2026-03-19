using WorldServerV2.World.Components;
using WorldServerV2.World.Spatial;

namespace WorldServerV2.World.Entities;

/// <summary>
/// Abstract base for all entities in the game world. Provides identity, position,
/// and a component bag for optional, dynamically-attached behaviors (guild, crafting,
/// scenarios, tactics, etc.).
/// <para>
/// The hierarchy is intentionally shallow and sealed at the leaf level:
/// <code>
/// WorldEntity (abstract) — ObjectId, Name, Position, optional component bag
/// ├── UnitEntity (abstract) — Health (direct field), Level, Realm
/// │   ├── PlayerEntity (sealed) — Character record, DisconnectType
/// │   ├── CreatureEntity (sealed) — Proto, Spawn
/// │   └── PetEntity (sealed) — Proto, Spawn, Owner
/// └── GameObjectEntity (sealed) — Entry, VfxState
/// </code>
/// Required state is expressed as direct fields on the appropriate subclass.
/// Optional behaviors are composed via <see cref="IComponent"/> in the bag.
/// </para>
/// </summary>
public abstract class WorldEntity
{
    private readonly Dictionary<Type, IComponent> _components = new();
    private ITickable[]? _tickableCache;

    protected WorldEntity(ushort objectId, EntityType type, string name)
    {
        ObjectId = objectId;
        Type = type;
        Name = name;
    }

    // ── Identity ────────────────────────────────────────────────────────

    /// <summary>
    /// Runtime object identifier (protocol-level, ushort range).
    /// Assigned by the <see cref="Region"/> when the entity enters the world
    /// and released when it is removed.
    /// </summary>
    public ushort ObjectId { get; private set; }

    /// <summary>
    /// Assigns (or clears) the OID for this entity. Called by the <see cref="Region"/>
    /// during add/remove — not intended for direct use by game systems.
    /// </summary>
    internal void AssignOid(ushort oid) => ObjectId = oid;

    /// <summary>The concrete entity type discriminator.</summary>
    public EntityType Type { get; }

    /// <summary>Display name shown to other players.</summary>
    public string Name { get; set; }

    // ── Position ────────────────────────────────────────────────────────

    /// <summary>Current position in the world. Updated by movement systems.</summary>
    public WorldPosition Position { get; set; }

    // ── Visibility ──────────────────────────────────────────────────────

    /// <summary>
    /// Cached set of nearby entities, maintained by the <see cref="Region"/> during its tick.
    /// Game systems read this freely; only the region's visibility update writes to it.
    /// </summary>
    public VisibilitySet Visibility { get; } = new();

    /// <summary>
    /// Position at the time of the last visibility scan. Used by the region to determine
    /// if the entity has moved far enough to warrant a re-scan.
    /// </summary>
    internal WorldPosition LastVisibilityCheckPosition { get; set; }

    // ── Optional Component Bag ──────────────────────────────────────────

    /// <summary>
    /// Gets the optional component of type <typeparamref name="T"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">If the component is not attached.</exception>
    public T Get<T>() where T : class, IComponent
    {
        if (_components.TryGetValue(typeof(T), out var component))
            return (T)component;

        throw new InvalidOperationException(
            $"Entity {ObjectId} ({Type}) does not have component {typeof(T).Name}.");
    }

    /// <summary>
    /// Gets the optional component of type <typeparamref name="T"/>, or <c>null</c> if not present.
    /// </summary>
    public T? TryGet<T>() where T : class, IComponent
    {
        return _components.TryGetValue(typeof(T), out var component) ? (T)component : null;
    }

    /// <summary>Returns <c>true</c> if an optional component of type <typeparamref name="T"/> is attached.</summary>
    public bool Has<T>() where T : class, IComponent
    {
        return _components.ContainsKey(typeof(T));
    }

    /// <summary>
    /// Attaches an optional component. Each concrete type may only be attached once.
    /// </summary>
    /// <exception cref="InvalidOperationException">If a component of that type is already attached.</exception>
    public void Attach<T>(T component) where T : class, IComponent
    {
        ArgumentNullException.ThrowIfNull(component);

        var type = typeof(T);
        if (_components.ContainsKey(type))
            throw new InvalidOperationException(
                $"Entity {ObjectId} already has component {type.Name}.");

        _components[type] = component;
        _tickableCache = null;
        component.OnAttach(this);
    }

    /// <summary>
    /// Detaches the optional component of type <typeparamref name="T"/>.
    /// Returns <c>true</c> if a component was removed, <c>false</c> if none was present.
    /// </summary>
    public bool Detach<T>() where T : class, IComponent
    {
        if (!_components.Remove(typeof(T), out var component))
            return false;

        _tickableCache = null;
        component.OnDetach();
        return true;
    }

    /// <summary>The number of optional components currently attached.</summary>
    public int ComponentCount => _components.Count;

    // ── Tick ────────────────────────────────────────────────────────────

    /// <summary>
    /// Ticks subclass-specific state (override in <see cref="UnitEntity"/> for health regen, etc.)
    /// then ticks all <see cref="ITickable"/> optional components.
    /// </summary>
    public virtual void Update(long tick)
    {
        _tickableCache ??= BuildTickableCache();

        foreach (var tickable in _tickableCache)
            tickable.Update(tick);
    }

    private ITickable[] BuildTickableCache()
    {
        var count = 0;
        foreach (var c in _components.Values)
        {
            if (c is ITickable)
                count++;
        }

        if (count == 0)
            return [];

        var result = new ITickable[count];
        var i = 0;
        foreach (var c in _components.Values)
        {
            if (c is ITickable t)
                result[i++] = t;
        }

        return result;
    }
}
