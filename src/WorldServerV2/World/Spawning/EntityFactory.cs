using WorldServerV2.Data;
using WorldServerV2.Data.Domain;
using WorldServerV2.World.Components;
using WorldServerV2.World.Entities;

namespace WorldServerV2.World.Spawning;

/// <summary>
/// Concrete <see cref="IEntityFactory"/> implementation.
/// <para>
/// Stats (HP, level range) are derived directly from prototype data for now.
/// When System 4 (Combat &amp; Stats) is built, replace the stub health formula with
/// <c>statService.ComputeCreatureStats(entity)</c>.
/// </para>
/// </summary>
public sealed class EntityFactory(IGameDataStore gameData) : IEntityFactory
{
    private readonly Random _rng = Random.Shared;

    // ── IEntityFactory ───────────────────────────────────────────────────

    /// <inheritdoc />
    public CreatureEntity CreateCreature(SpawnDescriptor descriptor)
    {
        if (!gameData.Creatures.Protos.TryGetValue(descriptor.Entry, out var proto))
            throw new InvalidOperationException(
                $"No creature prototype found for entry {descriptor.Entry}.");

        var level   = descriptor.LevelOverride   ?? PickLevel(proto.MinLevel, proto.MaxLevel);
        var faction = descriptor.FactionOverride ?? proto.Faction;

        // TODO (System 4): Replace with statService.ComputeCreatureStats
        var maxHealth = ComputeStubHealth(proto.WoundsModifier, level);

        var entity = new CreatureEntity(objectId: 0, proto, maxHealth)
        {
            Level   = level,
            Faction = faction,
            ModelId = (proto.Model2 != 0 && _rng.Next(2) == 0) ? proto.Model2 : proto.Model1,
            Scale   = PickScale(proto.MinScale, proto.MaxScale),
            Emote   = descriptor.EmoteOverride ?? proto.Emote,
        };

        AttachCreatureComponents(entity, proto, descriptor);

        return entity;
    }

    /// <inheritdoc />
    public GameObjectEntity CreateGameObject(GameObjectSpawnDescriptor descriptor)
    {
        // TODO: Look up GameObjectProto once GameObjectData domain is wired into IGameDataStore
        //       and use proto.Name as the nameOverride.
        var entity = new GameObjectEntity(
            objectId:  0,
            descriptor: descriptor);

        if (descriptor.DoorId != 0)
        {
            // Destructible doors get a health pool; stub HP until proto is wired.
            entity.Attach(new DestructibleComponent(maxHealth: 100_000u, doorId: descriptor.DoorId));
        }

        return entity;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private byte PickLevel(byte min, byte max)
        => min == max ? min : (byte)_rng.Next(min, max + 1);

    private ushort PickScale(ushort min, ushort max)
        => min == max ? min : (ushort)_rng.Next(min, max + 1);

    private static uint ComputeStubHealth(float woundsModifier, byte level)
    {
        // Stub formula: a minimal viable HP pool until System 4 provides real stats.
        const uint BaseHp = 200u;
        return Math.Max(1u, (uint)(BaseHp * (woundsModifier > 0 ? woundsModifier : 1f) * level));
    }

    private void AttachCreatureComponents(CreatureEntity entity, Data.Entities.CreatureProto proto, SpawnDescriptor descriptor)
    {
        // Equipment — visual items from creature_items table
        if (gameData.Creatures.Items.TryGetValue(proto.Entry, out var items))
            entity.Attach(new EquipmentComponent(items));

        // Waypoint movement — stub until System 5 (AI) is built
        // if (proto.IsWandering != 0) entity.Attach(new MovementComponent());

        // AI brain — stub until System 5 (AI) is built
        // if (!string.IsNullOrEmpty(proto.ScriptName)) entity.Attach(new BrainComponent(proto.ScriptName));

        // Vendor — stub until vendor system is built
        // if (proto.VendorId != 0) entity.Attach(new VendorComponent(proto.VendorId));

        // Suppress unused-parameter warning while stubs are commented out
        _ = descriptor;
    }
}
