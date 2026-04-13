using Core.GameWorld.Spatial;

namespace Core.GameWorld.Events;

/// <summary>
/// Marker interface for events emitted by entity subsystems during their tick.
/// <para>
/// Entities yield <see cref="ITickEvent"/> instances through the <c>emit</c> callback
/// passed to <see cref="Entities.WorldEntity.Update"/>. The region collects these
/// after ticking all entities and dispatches them through
/// <see cref="Spatial.IRegionEventDispatcher"/> without interpreting the event contents.
/// </para>
/// <para>
/// All existing region event record structs (e.g. <see cref="AbilityCastCompleted"/>)
/// implement this interface so they can flow through both the action dispatch path
/// (Phase 2 — direct <c>Dispatch&lt;T&gt;</c>) and the tick-emit path (Phase 3 — non-generic).
/// </para>
/// </summary>
public interface ITickEvent;