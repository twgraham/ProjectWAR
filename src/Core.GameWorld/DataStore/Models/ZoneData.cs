using System.Collections.Frozen;
using Core.Domain.Entities;

namespace Core.GameWorld.DataStore.Models;

/// <summary>
/// Immutable bundle of all zone/map-related game data.
/// </summary>
/// <param name="Infos">Zone definitions keyed by <see cref="ZoneInfo.ZoneId"/>.</param>
/// <param name="Jumps">Zone travel points keyed by <see cref="ZoneJump.Entry"/>.</param>
public readonly record struct ZoneData(
    FrozenDictionary<ushort, ZoneInfo> Infos,
    FrozenDictionary<uint, ZoneJump> Jumps);
