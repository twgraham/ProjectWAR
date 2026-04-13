using Core.GameWorld.Components;
using Core.GameWorld.Entities;

namespace Core.GameWorld.Combat.Career;

/// <summary>
/// Career mechanic resource interface. Each career archetype implements this
/// with distinct generation/consumption/decay semantics parameterized by config.
/// <para>
/// Attached to <see cref="PlayerEntity"/> via <see cref="CareerResourceComponent"/>.
/// Ticked automatically by the entity update loop.
/// </para>
/// </summary>
public interface ICareerResource : ITickable
{
    /// <summary>Current resource value.</summary>
    byte Current { get; }

    /// <summary>Maximum resource value.</summary>
    byte Max { get; }

    /// <summary>
    /// Derived level from current value (e.g. <c>Current / 25</c> for continuous bars,
    /// or a direct threshold lookup).
    /// </summary>
    byte Level { get; }

    /// <summary>Returns <c>true</c> if the entity has at least <paramref name="cost"/> resource.</summary>
    bool HasResource(int cost);

    /// <summary>Consume the given amount of resource. Returns <c>false</c> if insufficient.</summary>
    bool Consume(int amount);

    /// <summary>Generate (add) the given amount of resource, clamped to <see cref="Max"/>.</summary>
    void Generate(int amount);

    /// <summary>
    /// Notify the resource that an action occurred (e.g. ability cast, damage dealt).
    /// Resets idle timers for archetypes that decay on inactivity.
    /// </summary>
    void NotifyAction(long tick);

    /// <summary>Force-set the current resource to a specific value.</summary>
    void SetResource(byte value);
}
