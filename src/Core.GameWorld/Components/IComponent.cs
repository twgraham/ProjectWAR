using Core.GameWorld.Entities;

namespace Core.GameWorld.Components;

/// <summary>
/// A composable data or behavior block attached to a <see cref="WorldEntity"/>.
/// Components are the building blocks of the entity-component model, replacing
/// the legacy deep inheritance hierarchy and <c>BaseInterface</c> system.
/// <para>
/// <b>Convention</b>: Components represent optional, dynamically-attached behaviors
/// (what an entity <em>does</em>) — e.g. guild membership, crafting state, scenario tracking.
/// Required identity/state is expressed directly on the sealed entity subclasses.
/// </para>
/// </summary>
public interface IComponent
{
    /// <summary>The entity this component is attached to, or <c>null</c> if detached.</summary>
    WorldEntity? Owner { get; }

    /// <summary>
    /// Called when the component is attached to an entity via <see cref="WorldEntity.Attach{T}"/>.
    /// Implementations should store the <paramref name="entity"/> reference.
    /// </summary>
    void OnAttach(WorldEntity entity);

    /// <summary>
    /// Called when the component is detached from an entity via <see cref="WorldEntity.Detach{T}"/>.
    /// Implementations should clear the owner reference and release any resources.
    /// </summary>
    void OnDetach();
}

/// <summary>
/// Convenience base class for <see cref="IComponent"/> implementations.
/// Manages the <see cref="Owner"/> back-reference automatically.
/// </summary>
public abstract class ComponentBase : IComponent
{
    /// <inheritdoc />
    public WorldEntity? Owner { get; private set; }

    /// <inheritdoc />
    public virtual void OnAttach(WorldEntity entity) => Owner = entity;

    /// <inheritdoc />
    public virtual void OnDetach() => Owner = null;
}
