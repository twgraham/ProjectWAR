namespace Core.GameWorld.Components;

/// <summary>
/// Opt-in interface for components that need per-frame updates.
/// Only components implementing <see cref="ITickable"/> are invoked during
/// the world update loop — idle components pay zero tick cost.
/// <para>
/// This replaces the legacy model where every <c>BaseInterface</c> was ticked
/// unconditionally, even on idle or dead entities.
/// </para>
/// </summary>
public interface ITickable
{
    /// <summary>
    /// Called once per world tick.
    /// </summary>
    /// <param name="tick">Current tick timestamp in milliseconds.</param>
    void Update(long tick);
}
