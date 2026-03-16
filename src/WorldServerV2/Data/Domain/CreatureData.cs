using System.Collections.Frozen;
using WorldServerV2.Data.Entities;

namespace WorldServerV2.Data.Domain;

/// <summary>
/// Immutable bundle of all creature-related game data.
/// </summary>
/// <param name="Protos">Creature prototypes keyed by <see cref="CreatureProto.Entry"/>.</param>
/// <param name="Spawns">Creature spawn points keyed by <see cref="CreatureSpawn.Guid"/>.</param>
public readonly record struct CreatureData(
    FrozenDictionary<uint, CreatureProto> Protos,
    FrozenDictionary<uint, CreatureSpawn> Spawns);
