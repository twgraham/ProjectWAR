using Core.Domain.Entities;
using Core.GameWorld.Entities;
using Core.Session;

namespace Core.GameWorld.Events;

// Visibility lifecycle
public readonly record struct EntityBecameVisible(GameSession Observer, WorldEntity Entity, ZoneInfo Zone);
public readonly record struct EntityLeftVisibility(GameSession Observer, WorldEntity Entity);
public readonly record struct EntityStateChanged(GameSession Observer, WorldEntity Entity, ZoneInfo Zone);