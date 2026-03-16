using System.Collections.Frozen;
using WorldServerV2.Data.Entities;

namespace WorldServerV2.Data.Domain;

/// <summary>
/// Immutable bundle of all item-related game data.
/// </summary>
/// <param name="Infos">All item definitions keyed by <see cref="ItemInfo.Entry"/>.</param>
public readonly record struct ItemData(
    FrozenDictionary<uint, ItemInfo> Infos);
