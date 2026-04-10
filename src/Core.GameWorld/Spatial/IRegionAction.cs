namespace Core.GameWorld.Spatial;

/// <summary>
/// A unit of game logic that executes on the region thread with full access to
/// entity internals. Created by services in WorldServerV2, executed by the region.
/// <para>
/// Actions represent player intent (cast ability, interact, use item) or system-driven
/// mutations (spawn trigger, script event). They are enqueued from handler threads
/// and processed during the region's command drain phase, before entity ticks.
/// </para>
/// </summary>
public interface IRegionAction
{
    /// <summary>
    /// Executes the action on the region thread. Implementations have full access to
    /// entity internals (stats, buffs, health mutation) and may dispatch region events
    /// via the context's <see cref="IRegionActionContext.Dispatcher"/>.
    /// </summary>
    /// <param name="context">Region infrastructure (entities, game data, event dispatcher).</param>
    /// <param name="tick">Current region tick timestamp in milliseconds.</param>
    void Execute(IRegionActionContext context, long tick);
}
