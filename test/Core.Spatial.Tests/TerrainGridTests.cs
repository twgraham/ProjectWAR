using Core.Spatial.Zone;

namespace Core.Spatial.Tests;

public class TerrainGridTests
{
    [Fact]
    public void GetHeight_InHoleArea_ReturnsNegativeOne()
    {
        // 256x256 holemap, all zeros -> everything is a hole.
        int holemapWidth = 256;
        int holemapHeight = 256;
        var holemap = new byte[holemapWidth * holemapHeight];

        int terrainWidth = 1025;
        int terrainHeight = 1025;
        var heightmap = new ushort[terrainWidth * terrainHeight];

        var terrain = new TerrainGrid(heightmap, terrainWidth, terrainHeight, holemap, holemapWidth, holemapHeight);

        Assert.Equal(-1, terrain.GetHeight(500, 500));
    }

    [Fact]
    public void GetHeight_FlatTerrain_ReturnsConstantHeight()
    {
        int holemapWidth = 256;
        int holemapHeight = 256;
        var holemap = new byte[holemapWidth * holemapHeight];
        Array.Fill(holemap, (byte)1); // No holes.

        int terrainWidth = 1025;
        int terrainHeight = 1025;
        var heightmap = new ushort[terrainWidth * terrainHeight];
        Array.Fill(heightmap, (ushort)1000);

        var terrain = new TerrainGrid(heightmap, terrainWidth, terrainHeight, holemap, holemapWidth, holemapHeight);

        int z = terrain.GetHeight(100, 100);

        // On a perfectly flat heightmap at 1000, we expect ~1000.
        Assert.InRange(z, 998, 1002);
    }

    [Fact]
    public void GetHeight_ClampsToBounds()
    {
        int holemapWidth = 256;
        int holemapHeight = 256;
        var holemap = new byte[holemapWidth * holemapHeight];
        Array.Fill(holemap, (byte)1);

        int terrainWidth = 1025;
        int terrainHeight = 1025;
        var heightmap = new ushort[terrainWidth * terrainHeight];
        Array.Fill(heightmap, (ushort)500);

        var terrain = new TerrainGrid(heightmap, terrainWidth, terrainHeight, holemap, holemapWidth, holemapHeight);

        // Edge values should not throw - coordinates are clamped.
        int z = terrain.GetHeight(0, 0);
        Assert.True(z >= 0);

        z = terrain.GetHeight(65535, 65535);
        Assert.True(z >= 0);
    }
}
