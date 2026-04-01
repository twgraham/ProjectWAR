using System.Collections.Frozen;
using System.Collections.Immutable;
using WorldServerV2.Data.Entities;

namespace WorldServerV2.Data.Domain;

/// <summary>
/// Immutable bundle of all creature-related game data.
/// </summary>
/// <param name="Protos">Creature prototypes keyed by <see cref="CreatureProto.Entry"/>.</param>
/// <param name="Spawns">Creature spawn points keyed by <see cref="CreatureSpawn.Guid"/>.</param>
/// <param name="Items">
/// Equipped visual items per creature entry. Keyed by <see cref="CreatureItem.Entry"/>,
/// each value contains the full set of equipment slots for that creature.
/// </param>
public readonly record struct CreatureData(
    FrozenDictionary<uint, CreatureProto> Protos,
    FrozenDictionary<uint, CreatureSpawn> Spawns,
    FrozenDictionary<uint, ImmutableArray<CreatureItem>> Items);
