using System.Numerics;
using System.Runtime.CompilerServices;
using Core.Spatial.Math;

namespace Core.Spatial.Zone;

/// <summary>
/// Provides terrain height (Z) lookups from a heightmap grid.
/// Performs bilinear interpolation by casting a ray downward onto the terrain quad.
/// </summary>
public sealed class TerrainGrid
{
    private readonly ushort[] _heightmap;
    private readonly byte[] _holemap;
    private readonly int _terrainWidth;
    private readonly int _terrainHeight;
    private readonly int _holemapWidth;
    private readonly int _holemapHeight;

    public TerrainGrid(ushort[] heightmap, int terrainWidth, int terrainHeight,
                       byte[] holemap, int holemapWidth, int holemapHeight)
    {
        _heightmap = heightmap;
        _terrainWidth = terrainWidth;
        _terrainHeight = terrainHeight;
        _holemap = holemap;
        _holemapWidth = holemapWidth;
        _holemapHeight = holemapHeight;
    }

    /// <summary>
    /// Returns the terrain height at the given local (x, y) position.
    /// Returns -1 if the position falls in a hole area.
    /// </summary>
    public int GetHeight(int x, int y)
    {
        // Check holemap (256-unit grid).
        int holeX = System.Math.Clamp((int)MathF.Floor(x / 256.0f), 0, 255);
        int holeY = System.Math.Clamp((int)MathF.Floor(y / 256.0f), 0, 255);

        if (_holemap[holeY * _holemapWidth + holeX] == 0)
            return -1;

        // Compute heightmap grid cell (64-unit grid).
        int gridX = System.Math.Clamp((int)MathF.Floor(x / 64.0f), 0, 1023);
        int gridY = System.Math.Clamp((int)MathF.Floor(y / 64.0f), 0, 1023);

        float z1 = _heightmap[gridY * _terrainWidth + gridX];
        float z2 = _heightmap[gridY * _terrainWidth + (gridX + 1)];
        float z3 = _heightmap[(gridY + 1) * _terrainWidth + (gridX + 1)];
        float z4 = _heightmap[(gridY + 1) * _terrainWidth + gridX];

        // Build the two triangles of this quad and ray-test downward to get precise height.
        var rayOrigin = new Vector3(x, y, 0xFFFF);
        var rayDir = new Vector3(0, 0, -1); // Straight down; normalize is identity for unit axis.
        rayDir = Vector3.Normalize(rayDir);

        // Triangle 1: (gridX, gridY, z1), (gridX+1, gridY, z2), (gridX, gridY+1, z4)
        var v0 = new Vector3(gridX * 64f, gridY * 64f, z1);
        var v1 = new Vector3(gridX * 64f + 64f, gridY * 64f, z2);
        var v2 = new Vector3(gridX * 64f, gridY * 64f + 64f, z4);

        if (RayIntersection.RayTriangle(rayOrigin, rayDir, v0, v1, v2, out float d, out _))
            return (int)MathF.Floor(0xFFFF - d);

        // Triangle 2: (gridX+1, gridY, z2), (gridX+1, gridY+1, z3), (gridX, gridY+1, z4)
        v0 = new Vector3(gridX * 64f + 64f, gridY * 64f, z2);
        v1 = new Vector3(gridX * 64f + 64f, gridY * 64f + 64f, z3);
        v2 = new Vector3(gridX * 64f, gridY * 64f + 64f, z4);

        if (RayIntersection.RayTriangle(rayOrigin, rayDir, v0, v1, v2, out d, out _))
            return (int)MathF.Floor(0xFFFF - d);

        // Fallback: average of corners.
        float avg = (z1 + z2 + z3 + z4) / 4f;
        return (int)((avg + z4) / 2f);
    }
}
