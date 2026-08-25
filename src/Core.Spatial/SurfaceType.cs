namespace Core.Spatial;

/// <summary>
/// Identifies the type of surface hit during an occlusion or terrain query.
/// Values match the binary zone file format surface type encoding.
/// </summary>
public enum SurfaceType
{
    Solid = 0,

    // Doors
    Door1 = 1,
    Door2 = 2,
    Door3 = 3,
    Door4 = 4,
    Door5 = 5,
    Door6 = 6,
    Door7 = 7,
    Door8 = 8,
    Door9 = 9,

    // Water
    WaterGeneric = 10,
    WaterRiver = 11,
    WaterHotspring = 12,
    WaterOcean = 13,
    WaterDirty = 14,
    WaterStream = 15,
    WaterTainted = 16,
    WaterBog = 17,
    WaterIcy = 18,
    WaterPoison = 19,
    WaterLake = 20,
    WaterMarsh = 21,
    WaterMuck = 22,

    // Lava
    Lava = 23,
    LavaMagma = 24,

    // Other
    Tar = 25,
    InstantDeath = 26,
    Fixture = 27,
    Terrain = 28,

    // Jumps
    Jump1 = 29,
    Jump2 = 30,
    Jump3 = 31,
    Jump4 = 32,
    Jump5 = 33,
    Jump6 = 34,
    Jump7 = 35,
}
