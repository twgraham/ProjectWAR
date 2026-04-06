using Core.GameWorld.Components;
using Core.GameWorld.Spatial;

namespace Core.GameWorld.Entities;

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
    public void AssignOid(OidReservation reservation) => AssignOid(reservation.Oid);

    /// <summary>The concrete entity type discriminator.</summary>
    public EntityType Type { get; }

    /// <summary>Display name shown to other players.</summary>
    public string Name { get; set; }

    // ── Position ────────────────────────────────────────────────────────

    /// <summary>Current position in the world. Updated by movement systems.</summary>
    public WorldPosition Position { get; set; }

    // ── Activation ──────────────────────────────────────────────────────

    /// <summary>
    /// Whether this entity is active and should participate in visibility notifications,
    /// state broadcasts, and gameplay interactions.
    /// <para>
    /// NPCs and game objects default to <c>true</c> — they are ready the moment they
    /// enter the region. Players default to <c>false</c> and become active when the
    /// client signals readiness via <c>F_DUMP_STATICS</c>.
    /// </para>
    /// <para>
    /// The <see cref="Region"/> checks this flag before sending entity-create packets
    /// to a player and before including a player in state broadcasts.
    /// </para>
    /// </summary>
    public bool IsActive { get; set; } = true;

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

    // ── State Broadcasting ──────────────────────────────────────────────

    /// <summary>
    /// When <c>true</c>, the next <see cref="TryRefresh"/> call will return <c>true</c>,
    /// causing the <see cref="Region"/> to broadcast this entity's state
    /// (<c>F_OBJECT_STATE</c>) to all players in its <see cref="Visibility"/> set.
    /// <para>
    /// Game systems (combat, movement, buff application) set this flag whenever they change
    /// observable state (health, position, heading).
    /// </para>
    /// </summary>
    internal bool StateDirty { get; set; }

    /// <summary>
    /// Tick timestamp (ms) at which the next keepalive <c>F_OBJECT_STATE</c> broadcast
    /// should be sent, even if no state has changed. Reset after every broadcast via
    /// <see cref="TryRefresh"/>.
    /// <para>
    /// V1 uses 40–50 seconds (randomized per entity). The client holds an object for
    /// approximately 1 minute, so the refresh must arrive before that timeout.
    /// </para>
    /// </summary>
    internal long NextStateRefresh { get; set; }

    /// <summary>
    /// Per-entity keepalive interval in milliseconds. Randomized at construction to
    /// stagger broadcasts across entities and avoid packet bursts.
    /// V1: <c>40000 + Random.Next(10000)</c> → 40–50 seconds.
    /// </summary>
    internal int StateRefreshInterval { get; } = 40_000 + Random.Shared.Next(10_000);

    /// <summary>
    /// Checks whether this entity needs a state broadcast and, if so, resets the
    /// dirty flag and advances the keepalive timer. Returns <c>true</c> when a
    /// broadcast is required, <c>false</c> otherwise.
    /// <para>
    /// Called once per tick by the <see cref="Region"/>'s broadcast phase.
    /// Encapsulates both the dirty-flag check and the keepalive timer expiry so
    /// that the region loop remains a simple dispatch.
    /// </para>
    /// </summary>
    internal bool TryRefresh(long tickMs)
    {
        if (tickMs >= NextStateRefresh)
            StateDirty = true;

        if (!StateDirty)
            return false;

        StateDirty = false;
        NextStateRefresh = tickMs + StateRefreshInterval;
        return true;
    }

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

    /// <summary>
    /// Enumerates all attached components. Used by the region to discover
    /// <see cref="Components.IVisibilityInitContributor"/> implementations generically.
    /// </summary>
    public IEnumerable<IComponent> Components => _components.Values;

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
