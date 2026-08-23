namespace Core.GameWorld.Spatial;

/// <summary>
/// Shared constants for the spatial partitioning system. All values are in game units
/// unless otherwise noted. The conversion factor is 12 game units = 1 foot.
/// </summary>
public static class RegionConstants
{
    /// <summary>
    /// Side length of a single cell in game units (4096).
    /// A cell covers 4096×4096 game units ≈ 341×341 feet.
    /// </summary>
    public const int CellSize = 4096;

    /// <summary>Maximum cell grid index (exclusive). The grid is 800×800.</summary>
    public const int MaxCellIndex = 800;

    /// <summary>
    /// Maximum visibility range in game units (400 feet × 12 units/ft = 4800).
    /// Entities beyond this distance are removed from visibility sets.
    /// </summary>
    public const int MaxVisibility = 4800;

    /// <summary>Squared <see cref="MaxVisibility"/> for branchless distance comparisons.</summary>
    public const long MaxVisibilitySquared = (long)MaxVisibility * MaxVisibility;

    /// <summary>
    /// Minimum movement in game units before triggering a visibility re-scan (100 units ≈ 8.3 feet).
    /// Prevents excessive re-scans when entities make small adjustments.
    /// </summary>
    public const int RangeUpdateThreshold = 100;

    /// <summary>Squared <see cref="RangeUpdateThreshold"/> for branchless distance comparisons.</summary>
    public const long RangeUpdateThresholdSquared = (long)RangeUpdateThreshold * RangeUpdateThreshold;

    /// <summary>
    /// Number of cells to scan in each direction from the entity's cell during visibility
    /// updates. A radius of 1 scans a 3×3 neighborhood (9 cells).
    /// </summary>
    public const int CellScanRadius = 1;

    /// <summary>Target region tick interval in milliseconds (50ms = 20Hz).</summary>
    public const int TickIntervalMs = 50;
    
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(TickIntervalMs);

    /// <summary>Conversion factor: 12 game units = 1 foot.</summary>
    public const int UnitsPerFoot = 12;

    /// <summary>
    /// Height offset (game units) added to the ray origin/target Z when checking line of sight.
    /// Approximates head-height so ground-level positions don't immediately occlude.
    /// 72 game units = 6 feet (matches V1 CHARACTER_HEIGHT).
    /// </summary>
    public const float CharacterHeight = 72f;

    /// <summary>Default base radius in feet for entities without proto data.</summary>
    public const float DefaultBaseRadiusFeet = 4.5f;

    /// <summary>
    /// Default heading arc (in degrees) for the "in-front" check used by melee range
    /// gating and defense rolls. 140° means ±70° from the observer's heading.
    /// </summary>
    public const float DefaultFrontArcDegrees = 140f;

    /// <summary>Maximum assignable OID value (ushort.MaxValue). OID 0 is reserved.</summary>
    public const ushort MaxOid = ushort.MaxValue;
}
