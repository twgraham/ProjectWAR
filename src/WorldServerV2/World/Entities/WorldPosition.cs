namespace WorldServerV2.World.Entities;

/// <summary>
/// Immutable value type representing an entity's position in the game world.
/// Uses zone-local "pin" coordinates matching the legacy <c>Point3D</c> convention.
/// </summary>
/// <param name="X">Zone-local X coordinate.</param>
/// <param name="Y">Zone-local Y coordinate.</param>
/// <param name="Z">Zone-local Z coordinate (height).</param>
/// <param name="Heading">Facing direction (0–4095).</param>
/// <param name="ZoneId">The zone this position belongs to.</param>
public readonly record struct WorldPosition(int X, int Y, int Z, ushort Heading, ushort ZoneId)
{
    /// <summary>A position at the origin with no zone.</summary>
    public static readonly WorldPosition Zero = default;
}
