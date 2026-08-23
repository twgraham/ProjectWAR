using Core.Spatial;

namespace Core.GameWorld.Spatial;

/// <summary>
/// Lightweight service bundle passed from a <see cref="Region"/> to each entity
/// it owns. Provides access to region-scoped services (occlusion, etc.) without
/// requiring a back-reference to the region itself.
/// <para>
/// Assigned by <see cref="Region.ExecuteAdd"/> and cleared on remove. Extension
/// methods on <see cref="Entities.UnitEntity"/> read this to resolve services like
/// line-of-sight without static singletons.
/// </para>
/// </summary>
/// <param name="Occlusion">
/// Occlusion provider for line-of-sight queries, or <c>null</c> when zone data
/// is not loaded. When <c>null</c>, LOS is assumed clear (graceful fallback).
/// </param>
public sealed record RegionServices(IOcclusionProvider? Occlusion);
