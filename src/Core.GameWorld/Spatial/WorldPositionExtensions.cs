using Core.GameWorld.Entities;
using Core.Spatial;

namespace Core.GameWorld.Spatial;

/// <summary>
/// Extension methods for spatial queries on <see cref="WorldPosition"/>.
/// <para>
/// These are the position-level building blocks — no entity dependency.
/// All distance methods return values in <b>feet</b> (12 game units = 1 foot).
/// </para>
/// </summary>
public static class WorldPositionExtensions
{
    // ── Heading conversion ──────────────────────────────────────────────

    private const float HeadingToRadians = MathF.PI * 2f / 4096f;

    // ═════════════════════════════════════════════════════════════════════
    //  DISTANCE
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns the 2D center-to-center distance between two positions, in feet.
    /// </summary>
    public static float RawDistanceTo2D(this WorldPosition self, WorldPosition other)
    {
        long dx = self.X - other.X;
        long dy = self.Y - other.Y;
        return (float)Math.Sqrt(dx * dx + dy * dy) / RegionConstants.UnitsPerFoot;
    }

    /// <summary>
    /// Returns the 3D center-to-center distance between two positions, in feet.
    /// </summary>
    public static float RawDistanceTo3D(this WorldPosition self, WorldPosition other)
    {
        long dx = self.X - other.X;
        long dy = self.Y - other.Y;
        long dz = self.Z - other.Z;
        return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz) / RegionConstants.UnitsPerFoot;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  FACING / ARC CHECK
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns <c>true</c> if <paramref name="targetPos"/> is within the frontal arc of
    /// <paramref name="self"/>. The arc is centred on the observer's heading and spans
    /// <paramref name="arcDegrees"/> total (±<c>arcDegrees/2</c>).
    /// <para>Default arc: 140° (matching V1's <c>IsObjectInFront(target, 140)</c>).</para>
    /// </summary>
    public static bool IsInFrontArc(
        this WorldPosition self,
        WorldPosition targetPos,
        float arcDegrees = RegionConstants.DefaultFrontArcDegrees)
    {
        float dx = targetPos.X - self.X;
        float dy = targetPos.Y - self.Y;

        // Same position → always "in front".
        if (dx == 0f && dy == 0f)
            return true;

        float angleToTarget = MathF.Atan2(dy, dx);

        // WAR headings: 0 = south (+Y), clockwise.
        // atan2 convention: 0 = east (+X), counter-clockwise.
        // Conversion: atan2_angle = PI/2 - heading_radians
        float headingRad = self.Heading * HeadingToRadians;
        float facingAngle = MathF.PI / 2f - headingRad;

        float diff = angleToTarget - facingAngle;
        diff = MathF.IEEERemainder(diff, MathF.PI * 2f);

        float halfArc = arcDegrees * (MathF.PI / 180f) / 2f;
        return MathF.Abs(diff) <= halfArc;
    }
}
