using System.Numerics;
using System.Runtime.InteropServices;
using Core.Spatial.KdTree;
using Microsoft.Extensions.Logging;

namespace Core.Spatial.Zone;

/// <summary>
/// Chunk types in the OCC binary zone file format.
/// </summary>
internal enum ChunkType
{
    Undefined = 0,
    Zone = 1,
    Nif = 2,
    Fixture = 3,
    Terrain = 4,
    Collision = 5,
    Bsp = 6,
    Region = 7,
    Water = 8,
}

/// <summary>
/// Raw triangle as stored in the binary zone file.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct RawTriangle
{
    public int I0;
    public int I1;
    public int I2;
    public int UniqueId;
}

/// <summary>
/// Reads binary zone data in the OCC chunk format and populates <see cref="ZoneData"/> records.
/// Accepts a <see cref="Stream"/> so callers control where data comes from (filesystem,
/// embedded resources, <see cref="System.IO.MemoryStream"/> in tests, etc.).
/// </summary>
internal static class ZoneFileReader
{
    /// <summary>
    /// Loads zone data from <paramref name="stream"/> and populates entries in <paramref name="zones"/>.
    /// A single stream may contain data for multiple zone IDs (terrain + region + collision chunks).
    /// The caller is responsible for disposing the stream.
    /// </summary>
    public static void Load(Stream stream, ZoneData?[] zones, int maxTrisPerLeaf, ILogger? log = null)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);

        // Read and validate header.
        byte c0 = reader.ReadByte();
        byte c1 = reader.ReadByte();
        byte c2 = reader.ReadByte();

        if (c0 != (byte)'O' || c1 != (byte)'C' || c2 != (byte)'C')
        {
            log?.LogWarning("Invalid zone file header");
            return;
        }

        byte version = reader.ReadByte();
        byte headerSize = reader.ReadByte();
        stream.Seek(headerSize, SeekOrigin.Begin);

        long fileSize = stream.Length;

        while (stream.Position < fileSize)
        {
            long chunkStart = stream.Position;
            var chunkType = (ChunkType)reader.ReadInt32();
            uint chunkSize = reader.ReadUInt32();
            long nextChunk = stream.Position + chunkSize;

            try
            {
                switch (chunkType)
                {
                    case ChunkType.Terrain:
                        ReadTerrainChunk(reader, zones, log);
                        break;
                    case ChunkType.Region:
                        ReadRegionChunk(reader, zones, chunkSize, log);
                        break;
                    case ChunkType.Collision:
                        ReadCollisionChunk(reader, zones, maxTrisPerLeaf, log);
                        break;
                    case ChunkType.Water:
                        ReadWaterCollisionChunk(reader, zones, maxTrisPerLeaf, log);
                        break;
                }
            }
            catch (EndOfStreamException)
            {
                log?.LogWarning("Truncated {ChunkType} chunk at offset {ChunkStart} (declared size {ChunkSize}) — skipping", chunkType, chunkStart, chunkSize);
            }
            catch (Exception ex)
            {
                log?.LogWarning(ex, "Error reading {ChunkType} chunk at offset {ChunkStart} — skipping", chunkType, chunkStart);
            }

            stream.Seek(nextChunk, SeekOrigin.Begin);
        }
    }

    /// <summary>Lock protecting lazy initialization of <c>zones[]</c> slots during parallel loading.</summary>
    private static readonly Lock s_zoneLock = new();

    private static ZoneData? EnsureZone(ZoneData?[] zones, int zoneId, ILogger? log = null)
    {
        if (zoneId < 0 || zoneId >= zones.Length)
        {
            log?.LogInformation("Zone ID {ZoneId} out of range (0–{ZonesLength}) — skipping", zoneId, zones.Length - 1);
            return null;
        }

        if (zones[zoneId] is not null)
            return zones[zoneId]!;

        lock (s_zoneLock)
        {
            zones[zoneId] ??= new ZoneData { ZoneId = zoneId };
            return zones[zoneId]!;
        }
    }

    private static void ReadTerrainChunk(BinaryReader reader, ZoneData?[] zones, ILogger? log)
    {
        int regionId = reader.ReadInt32();
        int zoneId = reader.ReadInt32();

        var zone = EnsureZone(zones, zoneId, log);
        if (zone is null) return;

        zone.RegionId = regionId;

        int terrainWidth = reader.ReadInt32();
        int terrainHeight = reader.ReadInt32();
        int holemapWidth = reader.ReadInt32();
        int holemapHeight = reader.ReadInt32();

        // Read heightmap directly into the target array — avoids a temporary byte[] + BlockCopy.
        int terrainCount = terrainWidth * terrainHeight;
        var heightmap = new ushort[terrainCount];
        reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(heightmap.AsSpan()));

        // Read holemap directly into the target array.
        int holemapCount = holemapWidth * holemapHeight;
        var holemap = new byte[holemapCount];
        reader.BaseStream.ReadExactly(holemap);

        zone.Terrain = new TerrainGrid(heightmap, terrainWidth, terrainHeight, holemap, holemapWidth, holemapHeight);
    }

    private static void ReadRegionChunk(BinaryReader reader, ZoneData?[] zones, uint chunkSize, ILogger? log)
    {
        int regionId = reader.ReadInt32();

        // Guard: the chunk must have at least 8 bytes (regionId + zoneCount).
        if (chunkSize < 8)
        {
            log?.LogInformation("Region chunk too small ({ChunkSize} bytes) — skipping", chunkSize);
            return;
        }

        int zoneCount = reader.ReadInt32();

        // Each zone entry is 5 int32s = 20 bytes. Validate against remaining chunk data.
        long expectedBytes = (long)zoneCount * 20;
        if (expectedBytes > chunkSize - 8)
        {
            log?.LogWarning("Region chunk declares {ZoneCount} zones but only has {ChunkSize} bytes remaining — skipping", zoneCount, chunkSize - 8);
            return;
        }

        for (int i = 0; i < zoneCount; i++)
        {
            int zoneId = reader.ReadInt32();
            int offsetX = reader.ReadInt32();
            int offsetY = reader.ReadInt32();
            int nifCount = reader.ReadInt32();
            int fixtureCount = reader.ReadInt32();

            var zone = EnsureZone(zones, zoneId, log);
            if (zone is null) continue;

            zone.RegionId = regionId;
            zone.OffsetX = offsetX;
            zone.OffsetY = offsetY;
        }
    }

    private static void ReadCollisionChunk(BinaryReader reader, ZoneData?[] zones, int maxTrisPerLeaf, ILogger? log)
    {
        int regionId = reader.ReadInt32();
        int zoneId = reader.ReadInt32();

        var zone = EnsureZone(zones, zoneId, log);
        if (zone is null) return;

        zone.RegionId = regionId;

        // Read vertices.
        int vertexCount = reader.ReadInt32();
        var vertices = ReadVector3Array(reader, vertexCount);

        // Read fixture/triangle metadata.
        int fixtureCount = reader.ReadInt32();
        int triangleCount = reader.ReadInt32();
        int indexSize = reader.ReadInt32();

        // Read raw triangles.
        var rawTriangles = ReadRawTriangles(reader, triangleCount);

        if (triangleCount == 0)
        {
            log?.LogInformation("Zone {ZoneId} collision chunk has 0 triangles — skipping KD-tree build", zoneId);
            return;
        }

        // Build triangle index array and fixture map.
        // Bounds are accumulated inline — single pass, no second iteration over vertices.
        var triangles = new Vector3[triangleCount];
        var triangleIds = new int[triangleCount];

        int lastUniqueId = rawTriangles[0].UniqueId;
        var currentFixture = CreateFixture(rawTriangles[0], 0);
        zone.Fixtures[lastUniqueId] = currentFixture;
        zone.FixtureList.Add(currentFixture);

        for (int i = 0; i < triangleCount; i++)
        {
            ref var raw = ref rawTriangles[i];

            if (raw.UniqueId != lastUniqueId)
            {
                lastUniqueId = raw.UniqueId;
                currentFixture = CreateFixture(raw, i);
                zone.Fixtures[lastUniqueId] = currentFixture;
                zone.FixtureList.Add(currentFixture);
            }

            triangles[i] = new Vector3(raw.I0, raw.I1, raw.I2);
            triangleIds[i] = raw.UniqueId;
            currentFixture.TriangleCount++;

            var v0 = vertices[raw.I0];
            var v1 = vertices[raw.I1];
            var v2 = vertices[raw.I2];
            currentFixture.BoundsMin = Vector3.Min(currentFixture.BoundsMin, Vector3.Min(v0, Vector3.Min(v1, v2)));
            currentFixture.BoundsMax = Vector3.Max(currentFixture.BoundsMax, Vector3.Max(v0, Vector3.Max(v1, v2)));
        }

        // Build KD-tree.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        zone.CollisionTree = KdTreeAccel.Build(triangles, vertices, triangleIds, maxTrisPerLeaf);
        sw.Stop();

        log?.LogInformation("Loaded zone {ZoneId} collision ({TriangleCount} tris, {VertexCount} verts) in {ElapsedMilliseconds}ms", zoneId, triangleCount, vertexCount, sw.ElapsedMilliseconds);
    }

    private static void ReadWaterCollisionChunk(BinaryReader reader, ZoneData?[] zones, int maxTrisPerLeaf, ILogger? log)
    {
        int regionId = reader.ReadInt32();
        int zoneId = reader.ReadInt32();

        var zone = EnsureZone(zones, zoneId, log);
        if (zone is null) return;

        zone.RegionId = regionId;

        int vertexCount = reader.ReadInt32();
        var vertices = ReadVector3Array(reader, vertexCount);

        int fixtureCount = reader.ReadInt32();
        int triangleCount = reader.ReadInt32();
        int indexSize = reader.ReadInt32();

        var rawTriangles = ReadRawTriangles(reader, triangleCount);

        if (triangleCount == 0)
        {
            log?.LogInformation("Zone {ZoneId} water chunk has 0 triangles — skipping KD-tree build", zoneId);
            return;
        }

        var triangles = new Vector3[triangleCount];
        var triangleIds = new int[triangleCount];

        int lastUniqueId = rawTriangles[0].UniqueId;
        var firstFixture = CreateFixture(rawTriangles[0], 0);
        zone.Fixtures.TryAdd(lastUniqueId, firstFixture);

        for (int i = 0; i < triangleCount; i++)
        {
            ref var raw = ref rawTriangles[i];
            triangleIds[i] = raw.UniqueId;

            if (raw.UniqueId != lastUniqueId)
            {
                lastUniqueId = raw.UniqueId;
                var fixture = CreateFixture(raw, i);
                zone.Fixtures.TryAdd(lastUniqueId, fixture);
            }

            triangles[i] = new Vector3(raw.I0, raw.I1, raw.I2);
            zone.Fixtures[raw.UniqueId].TriangleCount++;
        }

        zone.WaterTree = KdTreeAccel.Build(triangles, vertices, triangleIds, maxTrisPerLeaf);
    }

    private static Fixture CreateFixture(in RawTriangle firstTri, int startIndex) => new()
    {
        TriangleStartIndex = startIndex,
        TriangleCount = 0,
        SurfaceType = firstTri.UniqueId >> 24,
        Id = firstTri.UniqueId & 0xFFFFFF,
        BoundsMin = new Vector3(float.MaxValue),
        BoundsMax = new Vector3(float.MinValue),
    };

    private static Vector3[] ReadVector3Array(BinaryReader reader, int count)
    {
        var result = new Vector3[count];
        reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(result.AsSpan()));
        return result;
    }

    private static RawTriangle[] ReadRawTriangles(BinaryReader reader, int count)
    {
        var result = new RawTriangle[count];
        reader.BaseStream.ReadExactly(MemoryMarshal.AsBytes(result.AsSpan()));
        return result;
    }
}
