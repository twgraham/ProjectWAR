using WorldServerV2.Data.Domain;

namespace WorldServerV2.Data;

/// <summary>
/// Concrete <see cref="IGameDataStore"/> backed by an immutable snapshot.
/// <para>
/// The <see cref="GameDataLoader"/> populates this store once during startup via
/// <see cref="Initialize"/>. After initialization, all property access is lock-free.
/// The snapshot can later be atomically swapped for hot-reload scenarios.
/// </para>
/// </summary>
public sealed class GameDataStore : IGameDataStore
{
    private volatile Snapshot? _snapshot;

    /// <inheritdoc />
    public ClassData Classes => Current.Classes;
    
    /// <inheritdoc />
    public ItemData Items => Current.Items;

    /// <inheritdoc />
    public CreatureData Creatures => Current.Creatures;

    /// <inheritdoc />
    public ZoneData Zones => Current.Zones;

    /// <inheritdoc />
    public CareerStatData CareerStats => Current.CareerStats;

    /// <inheritdoc />
    public AbilityData Abilities => Current.Abilities;

    /// <summary>
    /// Sets the data snapshot. May only be called once (typically by <see cref="GameDataLoader"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if already initialized.</exception>
    internal void Initialize(Snapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (Interlocked.CompareExchange(ref _snapshot, snapshot, null) is not null)
            throw new InvalidOperationException("Game data store has already been initialized.");
    }

    private Snapshot Current =>
        _snapshot ?? throw new InvalidOperationException("Game data has not been loaded yet.");

    /// <summary>
    /// Immutable point-in-time capture of all game data collections.
    /// </summary>
    internal sealed record Snapshot(
        ClassData Classes,
        ItemData Items,
        CreatureData Creatures,
        ZoneData Zones,
        CareerStatData CareerStats,
        AbilityData Abilities);
}
