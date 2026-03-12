using WorldServerV2.Data.Domain;

namespace WorldServerV2.Data;

/// <summary>
/// Read-only facade over all static game data loaded from the World database.
/// <para>
/// Consumers inject this interface to look up items, creatures, zones, and other
/// immutable reference data. All collections are frozen after the initial load,
/// guaranteeing zero contention under concurrent reads.
/// </para>
/// </summary>
public interface IGameDataStore
{
    /// <summary>Item definitions and item sets.</summary>
    ItemData Items { get; }

    /// <summary>Creature prototypes and spawn points.</summary>
    CreatureData Creatures { get; }

    /// <summary>Zone definitions and travel points.</summary>
    ZoneData Zones { get; }
}
