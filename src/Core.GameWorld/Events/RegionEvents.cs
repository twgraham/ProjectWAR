using Core.Domain.Entities;
using Core.GameWorld.Entities;

namespace Core.GameWorld.Events;

// Visibility lifecycle
public readonly record struct EntityBecameVisible(PlayerEntity Observer, WorldEntity Entity, ZoneInfo Zone);
public readonly record struct EntityLeftVisibility(PlayerEntity Observer, WorldEntity Entity);
public readonly record struct EntityStateChanged(PlayerEntity Observer, WorldEntity Entity, ZoneInfo Zone);