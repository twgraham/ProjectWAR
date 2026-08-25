using System.Diagnostics;
using System.Numerics;
using Microsoft.Extensions.Logging;

namespace Core.Spatial.Zone;

/// <summary>
/// Manages loaded zones and provides the high-level occlusion and terrain query API.
/// Implements <see cref="IOcclusionProvider"/>.
/// </summary>
/// <remarks>
/// Zone data is obtained through an <see cref="IZoneDataSource"/>, keeping this class
/// decoupled from any particular storage backend (filesystem, embedded resources, memory).
/// </remarks>
public sealed class ZoneManager : IOcclusionProvider, IDisposable
{
    private const int MaxZones = 500;
    private const int DefaultMaxTrisPerLeaf = 190;

    private readonly ZoneData?[] _zones = new ZoneData?[MaxZones];
    private readonly IZoneDataSource _dataSource;
    private readonly int _maxTrisPerLeaf;
    private readonly ILogger<ZoneManager> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public bool Initialized { get; private set; }

    /// <summary>
    /// Creates a <see cref="ZoneManager"/> backed by the given data source.
    /// </summary>
    public ZoneManager(IZoneDataSource dataSource, ILoggerFactory loggerFactory, int maxTrisPerLeaf = DefaultMaxTrisPerLeaf)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _maxTrisPerLeaf = maxTrisPerLeaf;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<ZoneManager>();
    }

    /// <summary>
    /// Convenience constructor that reads zone files from a filesystem directory.
    /// </summary>
    public ZoneManager(string basePath, ILoggerFactory loggerFactory, int maxTrisPerLeaf = DefaultMaxTrisPerLeaf)
        : this(new FileSystemZoneDataSource(basePath), loggerFactory, maxTrisPerLeaf) { }

    /// <summary>
    /// Loads all zone data from the configured data source.
    /// Independent zones are loaded in parallel for faster startup.
    /// Each file is processed in its own try-catch so a single malformed file
    /// does not prevent other zones from loading.
    /// </summary>
    public void InitZones()
    {
        if (Initialized)
            return;

        var streams = _dataSource.OpenAll().ToList();
        _logger.LogInformation("Loading {StreamsCount} zone file(s)...", streams.Count);

        var sw = Stopwatch.StartNew();
        int loaded = 0;
        int failed = 0;

        Parallel.ForEach(streams, new ParallelOptions { MaxDegreeOfParallelism = 5 }, stream =>
        {
            try
            {
                using (stream)
                    ZoneFileReader.Load(stream, _zones, _maxTrisPerLeaf, _loggerFactory.CreateLogger(typeof(ZoneFileReader)));

                Interlocked.Increment(ref loaded);
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                var name = stream is FileStream fs ? fs.Name : "(unknown)";
                _logger?.LogWarning(ex, "Failed to load zone file {Name}", name);
            }
        });

        sw.Stop();
        Initialized = true;

        if (failed > 0)
            _logger.LogWarning("Zone initialization complete — {Loaded} loaded, {Failed} failed, in {ElapsedMilliseconds}ms", loaded, failed, sw.ElapsedMilliseconds);
        else
            _logger.LogInformation("Zone initialization complete — {Loaded} file(s) loaded in {ElapsedMilliseconds}ms", loaded, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Loads a single zone by ID from the data source if not already loaded.
    /// </summary>
    public bool LoadZone(int zoneId)
    {
        if (zoneId is < 0 or >= MaxZones)
            return false;

        if (_zones[zoneId] != null)
            return true;

        using var stream = _dataSource.Open(zoneId);
        if (stream == null)
            return false;

        ZoneFileReader.Load(stream, _zones, _maxTrisPerLeaf, _loggerFactory.CreateLogger(typeof(ZoneFileReader)));
        return _zones[zoneId] != null;
    }

    /// <summary>
    /// Unloads a zone and frees its data.
    /// </summary>
    public void UnloadZone(int zoneId)
    {
        if (zoneId is >= 0 and < MaxZones)
            _zones[zoneId] = null;
    }

    /// <inheritdoc />
    public int GetTerrainZ(int zoneId, int x, int y)
    {
        if (zoneId is < 0 or >= MaxZones)
            return 0;

        var zone = _zones[zoneId];
        return zone?.Terrain?.GetHeight(x, y) ?? 0;
    }

    /// <inheritdoc />
    public OcclusionResult Raytest(
        int zoneId,
        float originX, float originY, float originZ,
        float targetX, float targetY, float targetZ,
        bool terrain, ref OcclusionInfo result)
    {
        return SegmentIntersect(zoneId, zoneId,
            originX, originY, originZ,
            targetX, targetY, targetZ,
            terrain, true, ref result);
    }

    /// <summary>
    /// Full segment intersection test. Tests geometry (fixtures) and optionally terrain/water.
    /// </summary>
    public OcclusionResult SegmentIntersect(
        int zoneIdA, int zoneIdB,
        float originX, float originY, float originZ,
        float targetX, float targetY, float targetZ,
        bool terrain, bool normalTest,
        ref OcclusionInfo result)
    {
        if (!EnsureLoaded(zoneIdA) || !EnsureLoaded(zoneIdB))
            return OcclusionResult.NotLoaded;

        var zoneA = _zones[zoneIdA]!;

        result.Result = OcclusionResult.NotOccluded;
        result.FixtureId = -1;

        bool terrainHit = false;

        if (terrain)
            terrainHit = TerrainIntersect(zoneIdA, zoneIdB, originX, originY, originZ, targetX, targetY, targetZ, ref result);

        // Transform to zone-local coordinates for geometry test.
        var from = new Vector3(
            0xFFFF - (originX - zoneA.OffsetX),
            originY - zoneA.OffsetY,
            originZ);

        var target = new Vector3(
            0xFFFF - (targetX - zoneA.OffsetX),
            targetY - zoneA.OffsetY,
            targetZ);

        var dir = Vector3.Normalize(target - from);
        double distance = Vector3.Distance(from, target);

        var collisionTree = zoneA.CollisionTree;
        if (collisionTree == null)
        {
            if (terrain && terrainHit)
                result.Result = OcclusionResult.OccludedByTerrain;
            return result.Result;
        }

        int hit = collisionTree.Intersect(from, dir, out float t, out var hitPoint, out var normal);

        if (hit != 0 && t <= distance && hitPoint.Z != 0)
        {
            // Skip back-facing normals by advancing along the ray (up to 10 attempts).
            if (normalTest && normal.Z < 0 && hit <= 0xFFFF)
            {
                int count = 0;
                while (hit != 0 && normal.Z < 0 && count < 10)
                {
                    var newOrigin = Vector3.Lerp(hitPoint, target, 0.001f);
                    hit = collisionTree.Intersect(newOrigin, dir, out t, out hitPoint, out normal);
                    count++;
                }
            }

            result.HitX = (0xFFFF - hitPoint.X) + zoneA.OffsetX;
            result.HitY = hitPoint.Y + zoneA.OffsetY;
            result.HitZ = hitPoint.Z;
            result.FixtureId = hit & 0xFFFFFF;
            result.SurfaceType = (SurfaceType)(hit >> 24);
            result.WaterDepth = 0;

            if (result.SurfaceType == 0)
                result.SurfaceType = SurfaceType.Fixture;

            result.Result = OcclusionResult.OccludedByGeometry;
            return OcclusionResult.OccludedByGeometry;
        }

        if (terrain && terrainHit)
            result.Result = OcclusionResult.OccludedByTerrain;

        return result.Result;
    }

    /// <summary>
    /// Terrain-only intersection. Steps along the ray checking terrain height at intervals.
    /// </summary>
    public bool TerrainIntersect(
        int zoneIdA, int zoneIdB,
        float originX, float originY, float originZ,
        float destX, float destY, float destZ,
        ref OcclusionInfo result)
    {
        if (!EnsureLoaded(zoneIdA) || !EnsureLoaded(zoneIdB))
            return false;

        var zoneA = _zones[zoneIdA]!;

        var from = new Vector3(
            originX - zoneA.OffsetX,
            originY - zoneA.OffsetY,
            originZ);

        var target = new Vector3(
            destX - zoneA.OffsetX,
            destY - zoneA.OffsetY,
            destZ);

        var dir = Vector3.Normalize(target - from);
        result.FixtureId = -1;

        int waterHit = 0;
        bool terrainHit = false;

        // Simple height test when origin and destination share the same X/Y.
        if (from.X == target.X && from.Y == target.Y)
        {
            if (zoneA.WaterTree != null)
            {
                waterHit = zoneA.WaterTree.Intersect(from, dir, out float wt, out var waterHitPoint, out _);
                float waterZ = 0xFFFF - wt;

                int height = GetTerrainZ(zoneIdA, (int)(originX - zoneA.OffsetX), (int)(originY - zoneA.OffsetY));
                terrainHit = true;

                if (waterHit == 0 || waterZ <= height)
                    waterHit = 0;

                if ((target.Z < from.Z && height > target.Z) || (target.Z > from.Z && height < target.Z))
                {
                    result.HitX = destX;
                    result.HitY = destY;
                    result.HitZ = height;
                }
            }
            else
            {
                int height = GetTerrainZ(zoneIdA, (int)(originX - zoneA.OffsetX), (int)(originY - zoneA.OffsetY));
                terrainHit = true;

                if ((target.Z < from.Z && height > target.Z) || (target.Z > from.Z && height < target.Z))
                {
                    result.HitX = destX;
                    result.HitY = destY;
                    result.HitZ = height;
                }
            }
        }

        if (!terrainHit)
        {
            double distance = Vector3.Distance(from, target);
            int incr = System.Math.Max(1, (int)(distance / 12.0));

            int heightStart = GetTerrainZ(zoneIdA, (int)from.X, (int)from.Y);
            int heightEnd = GetTerrainZ(zoneIdA, (int)target.X, (int)target.Y);

            // Only step if terrain is below both endpoints.
            if (heightStart < from.Z && heightEnd < target.Z)
            {
                double currentDist = 0;
                while (currentDist < distance)
                {
                    float lerpT = (float)(currentDist / distance);
                    var current = Vector3.Lerp(from, target, lerpT);
                    int height = GetTerrainZ(zoneIdA, (int)current.X, (int)current.Y);

                    if (height < 0)
                    {
                        terrainHit = false;
                        break;
                    }

                    if (height >= 0 && height > current.Z)
                    {
                        result.HitX = current.X + zoneA.OffsetX;
                        result.HitY = current.Y + zoneA.OffsetY;
                        result.HitZ = current.Z;
                        terrainHit = true;
                    }

                    currentDist += incr;
                }
            }
        }

        if (waterHit != 0 || terrainHit)
        {
            if (waterHit != 0)
            {
                // Water above terrain.
                if (zoneA.WaterTree != null)
                {
                    zoneA.WaterTree.Intersect(from, dir, out _, out var wHitPoint, out _);
                    if (wHitPoint.Z > result.HitZ)
                    {
                        result.WaterDepth = wHitPoint.Z - result.HitZ;
                        result.FixtureId = (waterHit & 0xFFFFFF) - 0xFFFF;
                        result.SurfaceType = (SurfaceType)(waterHit >> 24);
                        result.HitZ = wHitPoint.Z;
                        return true;
                    }
                }
            }

            result.SurfaceType = SurfaceType.Terrain;
            result.FixtureId = 0;
            result.WaterDepth = 0;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the number of fixtures loaded in a zone.
    /// </summary>
    public int GetFixtureCount(int zoneId)
    {
        if (zoneId < 0 || zoneId >= MaxZones)
            return 0;

        return _zones[zoneId]?.FixtureList.Count ?? 0;
    }

    /// <summary>
    /// Gets info about a fixture by its list index within the zone.
    /// </summary>
    public bool GetFixtureInfo(int zoneId, int index, ref FixtureInfo info)
    {
        if (zoneId < 0 || zoneId >= MaxZones)
            return false;

        var zone = _zones[zoneId];
        if (zone == null || index < 0 || index >= zone.FixtureList.Count)
            return false;

        var fixture = zone.FixtureList[index];

        // Apply the same X-axis flip as the C++ code.
        info.X1 = 0xFFFF - fixture.BoundsMax.X;
        info.Y1 = fixture.BoundsMin.Y;
        info.Z1 = fixture.BoundsMin.Z;
        info.X2 = 0xFFFF - fixture.BoundsMin.X;
        info.Y2 = fixture.BoundsMax.Y;
        info.Z2 = fixture.BoundsMax.Z;
        info.SurfaceType = fixture.SurfaceType;
        info.UniqueId = fixture.Id;
        return true;
    }

    /// <summary>
    /// Sets whether a fixture's triangles participate in collision queries.
    /// </summary>
    public bool SetFixtureVisible(int zoneId, uint uniqueId, byte instanceId, bool visible)
    {
        if (!EnsureLoaded(zoneId))
            return false;

        var zone = _zones[zoneId]!;
        int key = (instanceId << 24) | (int)uniqueId;

        if (!zone.Fixtures.TryGetValue(key, out var fixture))
        {
            _logger.LogInformation("Zone {ZoneId} does not contain fixture {UniqueId} instance {InstanceId}", zoneId, uniqueId, instanceId);
            return false;
        }

        fixture.Visible = visible;
        var tree = zone.CollisionTree;
        if (tree == null)
            return false;

        int end = fixture.TriangleStartIndex + fixture.TriangleCount;
        for (int i = fixture.TriangleStartIndex; i < end; i++)
            tree.SetTriangleVisible(i, visible);

        return true;
    }

    /// <summary>
    /// Returns whether a fixture's triangles are currently visible for collision.
    /// </summary>
    public bool GetFixtureVisible(int zoneId, uint uniqueId, byte instanceId)
    {
        if (!EnsureLoaded(zoneId))
            return false;

        var zone = _zones[zoneId]!;
        int key = (instanceId << 24) | (int)uniqueId;

        return zone.Fixtures.TryGetValue(key, out var fixture) && fixture.Visible;
    }

    /// <summary>
    /// Convenience overload that decodes a packed door ID into zone/fixture/instance components.
    /// </summary>
    public bool SetFixtureVisible(uint doorId, bool visible)
    {
        DecodeDoorId(doorId, out int zoneId, out uint uniqueId, out byte instanceId);
        return SetFixtureVisible(zoneId, uniqueId, instanceId, visible);
    }

    /// <summary>
    /// Convenience overload that decodes a packed door ID.
    /// </summary>
    public bool GetFixtureVisible(uint doorId)
    {
        DecodeDoorId(doorId, out int zoneId, out uint uniqueId, out byte instanceId);
        return GetFixtureVisible(zoneId, uniqueId, instanceId);
    }

    public void Dispose()
    {
        for (int i = 0; i < MaxZones; i++)
            _zones[i] = null;
    }

    private bool EnsureLoaded(int zoneId)
    {
        return zoneId >= 0 && zoneId < MaxZones && _zones[zoneId] != null;
    }

    private static void DecodeDoorId(uint doorId, out int zoneId, out uint uniqueId, out byte instanceId)
    {
        zoneId = ((int)doorId >> 20) & 0x3FF;
        uniqueId = (uint)(((((int)doorId >> 30) & 0x3) << 14) | (((int)doorId >> 6) & 0x3FFF));
        int doorIndex = ((int)doorId & 0x3F) - 0x28;
        instanceId = (byte)(doorIndex + 1);
    }
}
