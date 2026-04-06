using System.Collections.Frozen;
using Core.GameWorld.Entities;

namespace Core.GameWorld.DataStore.Models;

/// <summary>
/// Identifies a cell within a specific region — the key used to bucket spawn descriptors
/// for O(1) lookup when a cell is loaded.
/// </summary>
/// <param name="RegionId">The region this cell belongs to.</param>
/// <param name="CellX">Cell X index within the region grid (<c>regionAbsX / 4096</c>).</param>
/// <param name="CellY">Cell Y index within the region grid (<c>regionAbsY / 4096</c>).</param>
public readonly record struct CellKey(ushort RegionId, int CellX, int CellY);

/// <summary>
/// Unified factory input for spawning a creature. Shared by DB-driven and
/// programmatic (scripted, event-triggered) spawn paths.
/// </summary>
public readonly record struct SpawnDescriptor
{
    // ── Source ────────────────────────────────────────────────────────────

    /// <summary>Creature prototype entry ID.</summary>
    public required uint Entry { get; init; }

    // ── Position ──────────────────────────────────────────────────────────

    /// <summary>Region the creature is placed in (used for respawn re-queue).</summary>
    public required ushort RegionId { get; init; }

    /// <summary>Zone ID (used in <see cref="F_CREATE_MONSTER"/> header).</summary>
    public required ushort ZoneId { get; init; }

    /// <summary>World position where the creature spawns (and re-spawns).</summary>
    public required WorldPosition Position { get; init; }

    // ── Overrides ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fixed level override. When <c>null</c>, the factory picks randomly from
    /// <c>proto.MinLevel..proto.MaxLevel</c>.
    /// </summary>
    public byte? LevelOverride { get; init; }

    /// <summary>
    /// Fixed faction override. When <c>null</c>, the factory uses <c>proto.Faction</c>.
    /// </summary>
    public byte? FactionOverride { get; init; }

    /// <summary>
    /// Fixed emote override. When <c>null</c>, the factory uses <c>proto.Emote</c>
    /// falling back to the spawn record's emote if non-zero.
    /// </summary>
    public byte? EmoteOverride { get; init; }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    /// <summary>
    /// Milliseconds before the creature re-spawns after death.
    /// <c>0</c> means temporary — the creature is never re-queued by the respawn scheduler.
    /// </summary>
    public uint RespawnDelayMs { get; init; }

    /// <summary>
    /// The DB spawn GUID for this record. <c>null</c> for dynamic programmatic spawns
    /// that have no persistent identity.
    /// </summary>
    public uint? DbSpawnGuid { get; init; }
}

/// <summary>
/// Unified factory input for spawning a static game object.
/// </summary>
public readonly record struct GameObjectSpawnDescriptor
{
    // ── Source ────────────────────────────────────────────────────────────

    /// <summary>Game object prototype entry ID.</summary>
    public required uint Entry { get; init; }

    // ── Position ──────────────────────────────────────────────────────────

    /// <summary>Region the object is placed in.</summary>
    public required ushort RegionId { get; init; }

    /// <summary>Zone ID.</summary>
    public required ushort ZoneId { get; init; }

    /// <summary>World position where the object is placed.</summary>
    public required WorldPosition Position { get; init; }

    // ── State ─────────────────────────────────────────────────────────────

    /// <summary>Initial VFX state (e.g. door open/closed, lever state).</summary>
    public byte VfxState { get; init; }

    /// <summary>Whether the object can be interacted with.</summary>
    public bool Interactable { get; init; }

    /// <summary>Door identifier for keep/fort doors. <c>0</c> if not applicable.</summary>
    public uint DoorId { get; init; }

    // ── Raw spawn fields (wire protocol serialization) ─────────────────────

    /// <summary>Display model ID from the spawn record.</summary>
    public ushort DisplayId { get; init; }

    /// <summary>
    /// Raw unknown ushort[6] array from the spawn record (<c>unks</c> column).
    /// Index 0 encodes the hi-byte (UnkHi, dead on wire) and lo-byte (<see cref="Dtos.StaticFlags"/>).
    /// Indices 1-5 map to the wire protocol fields Unks1–Unks5.
    /// Null / short arrays are treated as all-zero.
    /// </summary>
    public ushort[]? Unks { get; init; }

    /// <summary>Raw Unk1 byte from spawn record (<c>unk1</c> column).</summary>
    public byte SpawnUnk1 { get; init; }

    /// <summary>Raw Unk2 byte from spawn record (<c>unk2</c> column).</summary>
    public byte SpawnUnk2 { get; init; }

    /// <summary>Raw Unk3 uint from spawn record (<c>unk3</c> column).</summary>
    public uint SpawnUnk3 { get; init; }

    /// <summary>Raw Unk4 uint from spawn record (<c>unk4</c> column).</summary>
    public uint SpawnUnk4 { get; init; }

    // ── Lifecycle ─────────────────────────────────────────────────────────

    /// <summary>
    /// DB spawn GUID. <c>null</c> for dynamic programmatic spawns.
    /// </summary>
    public uint? DbSpawnGuid { get; init; }
}

/// <summary>
/// Immutable bundle of pre-bucketed spawn descriptors keyed by <see cref="CellKey"/>
/// (regionId, cellX, cellY). Loaded once at startup; never mutated.
/// </summary>
/// <param name="Creatures">
/// Creature spawn descriptors, keyed by the cell they fall into.
/// </param>
/// <param name="GameObjects">
/// Game-object spawn descriptors, keyed by the cell they fall into.
/// </param>
public readonly record struct SpawnData(
    FrozenDictionary<CellKey, IReadOnlyList<SpawnDescriptor>> Creatures,
    FrozenDictionary<CellKey, IReadOnlyList<GameObjectSpawnDescriptor>> GameObjects)
{
    /// <summary>An empty instance with no spawn data.</summary>
    public static SpawnData Empty { get; } = new(
        FrozenDictionary<CellKey, IReadOnlyList<SpawnDescriptor>>.Empty,
        FrozenDictionary<CellKey, IReadOnlyList<GameObjectSpawnDescriptor>>.Empty);
}
