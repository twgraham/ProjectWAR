using WorldServerV2.World.Components;
using WorldServerV2.World.Entities;

namespace WorldServerV2.World.Combat.Career;

/// <summary>
/// Component wrapper that attaches an <see cref="ICareerResource"/> to a
/// <see cref="PlayerEntity"/>. Implements <see cref="ITickable"/> so the
/// resource is automatically ticked by the entity update loop.
/// </summary>
public sealed class CareerResourceComponent : ComponentBase, ITickable
{
    public CareerResourceComponent(ICareerResource resource)
    {
        Resource = resource ?? throw new ArgumentNullException(nameof(resource));
    }

    /// <summary>The underlying career resource implementation.</summary>
    public ICareerResource Resource { get; }

    /// <inheritdoc />
    public void Update(long tick) => Resource.Update(tick);
}
