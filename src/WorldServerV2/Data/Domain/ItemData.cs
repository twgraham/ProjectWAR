using System.Collections.Frozen;
using WorldServerV2.World.Items;

namespace WorldServerV2.Data.Domain;

/// <summary>
/// Immutable bundle of all item-related game data, loaded and parsed at startup.
/// </summary>
/// <param name="Definitions">All item definitions keyed by <see cref="ItemDefinition.Entry"/>, with pre-parsed stats/effects.</param>
/// <param name="Sets">All item set definitions keyed by <see cref="ItemSetDefinition.Entry"/>.</param>
public readonly record struct ItemData(
    FrozenDictionary<uint, ItemDefinition> Definitions,
    FrozenDictionary<uint, ItemSetDefinition> Sets);
