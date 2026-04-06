using Core.Domain.Entities;
using Core.GameWorld.DataStore.Models;
using Core.GameWorld.Entities;

namespace Core.GameWorld.DataStore.Providers;

/// <summary>
/// Static helpers that convert DB entity records into spawn descriptor value types.
/// The DB already stores region-absolute coordinates (matching the legacy WorldPosition.X = Spawn.WorldX
/// pattern), so <b>no zone-offset conversion</b> is needed. Zone information is used only
/// to resolve the region ID and zone ID for each descriptor.
/// </summary>
public static class SpawnDescriptorFactory
{
    /// <summary>
    /// Builds a <see cref="SpawnDescriptor"/> from a DB creature spawn record and its
    /// owning zone info.
    /// </summary>
    /// <param name="spawn">The DB spawn record.</param>
    /// <param name="zone">Zone the spawn belongs to — provides region ID and cell offsets.</param>
    public static SpawnDescriptor FromDbRecord(CreatureSpawn spawn, ZoneInfo zone)
    {
        // DB stores region-absolute coordinates (legacy: WorldPosition.X = Spawn.WorldX).
        // Do NOT add zone offsets — that would double-offset.
        var position = WorldPosition.FromRegionAbsolute(
            regionId: zone.Region,
            zoneId:   spawn.ZoneId,
            worldX:   spawn.WorldX,
            worldY:   spawn.WorldY,
            z:        spawn.WorldZ,
            heading:  (ushort)(spawn.WorldO & 0xFFFF));

        return new SpawnDescriptor
        {
            Entry           = spawn.Entry,
            RegionId        = zone.Region,
            ZoneId          = spawn.ZoneId,
            Position        = position,
            LevelOverride   = spawn.Level   != 0 ? spawn.Level   : null,
            FactionOverride = spawn.Faction != 0 ? spawn.Faction : null,
            EmoteOverride   = spawn.Emote   != 0 ? spawn.Emote   : null,
            RespawnDelayMs  = (uint)spawn.RespawnMinutes * 60_000u,
            DbSpawnGuid     = spawn.Guid,
        };
    }

    /// <summary>
    /// Builds a <see cref="GameObjectSpawnDescriptor"/> from a DB game-object spawn record
    /// and its owning zone info.
    /// </summary>
    /// <param name="spawn">The DB game-object spawn record.</param>
    /// <param name="zone">Zone the spawn belongs to.</param>
    public static GameObjectSpawnDescriptor FromDbRecord(GameObjectSpawn spawn, ZoneInfo zone)
    {
        // DB stores region-absolute coordinates (legacy: WorldPosition.X = Spawn.WorldX).
        // Do NOT add zone offsets — that would double-offset.
        var position = WorldPosition.FromRegionAbsolute(
            regionId: zone.Region,
            zoneId:   (ushort)spawn.ZoneId,
            worldX:   spawn.WorldX,
            worldY:   spawn.WorldY,
            z:        spawn.WorldZ,
            heading:  (ushort)(spawn.WorldO & 0xFFFF));

        return new GameObjectSpawnDescriptor
        {
            Entry        = (uint)spawn.Entry,
            RegionId     = zone.Region,
            ZoneId       = (ushort)spawn.ZoneId,
            Position     = position,
            VfxState     = (byte)Math.Min(255L, spawn.VfxState),
            Interactable = spawn.IsInteractable,
            DoorId       = spawn.DoorId.HasValue ? (uint)spawn.DoorId.Value : 0u,
            DbSpawnGuid  = (uint?)spawn.Guid,
            DisplayId    = (ushort)Math.Min(ushort.MaxValue, (long)spawn.DisplayId),
            Unks         = spawn.Unks,
            SpawnUnk1    = (byte)(spawn.Unk1 < 0 ? 0 : Math.Min(255, (int)spawn.Unk1)),
            SpawnUnk2    = (byte)(spawn.Unk2 < 0 ? 0 : Math.Min(255, (int)spawn.Unk2)),
            SpawnUnk3    = (uint)Math.Max(0L, spawn.Unk3),
            SpawnUnk4    = (uint)Math.Max(0L, spawn.Unk4),
        };
    }
}
