using System.Collections.Frozen;
using System.Collections.Immutable;
using Core.Domain.Entities;
using Core.Domain.ValueObjects;
using Core.GameWorld.DataStore;
using Core.GameWorld.DataStore.Models;
using Core.GameWorld.Entities;
using Core.GameWorld.Events;
using Core.GameWorld.Items;
using Core.GameWorld.Spatial;
using Core.GameWorld.Spawning;
using Core.Session;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using WorldServerV2.Network.Dtos;
using WorldServerV2.RegionHandlers;
using WorldServerV2.Telemetry;
using ItemData = Core.GameWorld.DataStore.Models.ItemData;

namespace WorldServerV2.Tests;

/// <summary>
/// Tests for <see cref="StationaryObjectStateResponse"/>, <see cref="MovingObjectStateResponse"/>,
/// <see cref="ObjectStateFlags"/>, the <see cref="WorldEntity.TryRefresh"/> method, and the
/// <see cref="Region.Tick"/> broadcast phase that sends <c>F_OBJECT_STATE</c> packets
/// to players in visibility range.
/// </summary>
public class ObjectStateTests
{
    // ═══════════════════════════════════════════════════════════════════
    // StationaryObjectStateResponse — Unit
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Stationary_unit_produces_correct_base_fields()
    {
        var creature = MakeCreature();
        creature.Position = MakePosition(zone: TestZoneId);

        var zone = MakeZoneInfo(TestZoneId, offX: 2, offY: 3);
        var resp = StationaryObjectStateResponse.From(creature, zone);

        resp.Oid.ShouldBe(creature.ObjectId);
        resp.Z.ShouldBe((ushort)creature.Position.Z);
        resp.PctHealth.ShouldBe(creature.Health.Percent);
        resp.Flags.ShouldBe((byte)ObjectStateFlags.None);
        resp.ZoneId.ShouldBe((byte)TestZoneId);
        resp.Unk1.ShouldBe((byte)0);
        resp.Unk2.ShouldBe(0u);
    }

    [Fact]
    public void Stationary_unit_converts_to_zone_local_coordinates()
    {
        const int offX = 2, offY = 3;
        const int regionX = offX * 4096 + 500;
        const int regionY = offY * 4096 + 700;
        var creature = MakeCreature();
        creature.Position = new WorldPosition(1, regionX, regionY, 100, 512, TestZoneId);

        var zone = MakeZoneInfo(TestZoneId, offX, offY);
        var resp = StationaryObjectStateResponse.From(creature, zone);

        resp.X.ShouldBe((ushort)500);
        resp.Y.ShouldBe((ushort)700);
    }

    [Fact]
    public void Stationary_unit_heading_is_set()
    {
        var creature = MakeCreature();
        const ushort heading = 2048;
        creature.Position = new WorldPosition(1, 5 * 4096, 5 * 4096, 0, heading, TestZoneId);

        var zone = MakeZoneInfo(TestZoneId, offX: 5, offY: 5);
        var resp = StationaryObjectStateResponse.From(creature, zone);

        resp.Heading.ShouldBe(heading);
    }

    // ═══════════════════════════════════════════════════════════════════
    // StationaryObjectStateResponse — Game Object
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Stationary_gameObject_has_100_percent_health()
    {
        var go = MakeGameObject();
        go.Position = MakePosition(zone: TestZoneId);

        var zone = MakeZoneInfo(TestZoneId, offX: 5, offY: 5);
        var resp = StationaryObjectStateResponse.From(go, zone);

        resp.PctHealth.ShouldBe((byte)100);
    }

    [Fact]
    public void Stationary_gameObject_has_no_movement_flags()
    {
        var go = MakeGameObject();
        go.Position = MakePosition(zone: TestZoneId);

        var zone = MakeZoneInfo(TestZoneId, offX: 5, offY: 5);
        var resp = StationaryObjectStateResponse.From(go, zone);

        resp.Flags.ShouldBe((byte)ObjectStateFlags.None);
    }

    [Fact]
    public void Stationary_gameObject_heading_is_set()
    {
        var go = MakeGameObject();
        const ushort heading = 1024;
        go.Position = new WorldPosition(1, 5 * 4096 + 100, 5 * 4096 + 200, 50, heading, TestZoneId);

        var zone = MakeZoneInfo(TestZoneId, offX: 5, offY: 5);
        var resp = StationaryObjectStateResponse.From(go, zone);

        resp.Heading.ShouldBe(heading);
    }

    // ═══════════════════════════════════════════════════════════════════
    // MovingObjectStateResponse — Unit
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Moving_sets_moving_flag()
    {
        var creature = MakeCreature();
        creature.Position = MakePosition(zone: TestZoneId);
        var zone = MakeZoneInfo(TestZoneId, offX: 5, offY: 5);

        var resp = MovingObjectStateResponse.From(creature, zone,
            speed: 200, destX: 100, destY: 200, destZ: 50, destZoneId: (byte)TestZoneId);

        ((ObjectStateFlags)resp.Flags).HasFlag(ObjectStateFlags.Moving).ShouldBeTrue();
    }

    [Fact]
    public void Moving_contains_typed_destination_fields()
    {
        var creature = MakeCreature();
        creature.Position = MakePosition(zone: TestZoneId);
        var zone = MakeZoneInfo(TestZoneId, offX: 5, offY: 5);

        const ushort speed = 200, destX = 100, destY = 200, destZ = 50;
        const byte destZone = 42;
        var resp = MovingObjectStateResponse.From(creature, zone,
            speed, destX, destY, destZ, destZone);

        resp.Speed.ShouldBe(speed);
        resp.DestUnk.ShouldBe((byte)0);
        resp.DestX.ShouldBe(destX);
        resp.DestY.ShouldBe(destY);
        resp.DestZ.ShouldBe(destZ);
        resp.DestZoneId.ShouldBe(destZone);
    }

    [Fact]
    public void Moving_has_correct_base_fields()
    {
        var creature = MakeCreature();
        creature.Position = MakePosition(zone: TestZoneId);
        var zone = MakeZoneInfo(TestZoneId, offX: 5, offY: 5);

        var resp = MovingObjectStateResponse.From(creature, zone,
            speed: 100, destX: 50, destY: 60, destZ: 70, destZoneId: (byte)TestZoneId);

        resp.Oid.ShouldBe(creature.ObjectId);
        resp.PctHealth.ShouldBe(creature.Health.Percent);
        resp.ZoneId.ShouldBe((byte)TestZoneId);
    }

    // ═══════════════════════════════════════════════════════════════════
    // WorldEntity.TryRefresh
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void TryRefresh_returns_true_when_dirty()
    {
        var creature = MakeCreature();
        creature.NextStateRefresh = long.MaxValue; // keepalive not due
        creature.StateDirty = true;

        creature.TryRefresh(1).ShouldBeTrue();
    }

    [Fact]
    public void TryRefresh_clears_dirty_flag()
    {
        var creature = MakeCreature();
        creature.NextStateRefresh = long.MaxValue;
        creature.StateDirty = true;

        creature.TryRefresh(1);

        creature.StateDirty.ShouldBeFalse();
    }

    [Fact]
    public void TryRefresh_advances_keepalive_timer()
    {
        var creature = MakeCreature();
        creature.StateDirty = true;

        creature.TryRefresh(1000);

        creature.NextStateRefresh.ShouldBeGreaterThanOrEqualTo(1000 + 40_000);
        creature.NextStateRefresh.ShouldBeLessThan(1000 + 50_000);
    }

    [Fact]
    public void TryRefresh_returns_false_when_clean_and_timer_not_expired()
    {
        var creature = MakeCreature();
        creature.NextStateRefresh = long.MaxValue;
        creature.StateDirty = false;

        creature.TryRefresh(1).ShouldBeFalse();
    }

    [Fact]
    public void TryRefresh_returns_true_when_keepalive_expires()
    {
        var creature = MakeCreature();
        creature.NextStateRefresh = 100;
        creature.StateDirty = false;

        creature.TryRefresh(100).ShouldBeTrue();
    }

    [Fact]
    public void TryRefresh_sets_dirty_on_timer_expiry_then_clears()
    {
        var creature = MakeCreature();
        creature.NextStateRefresh = 50;
        creature.StateDirty = false;

        creature.TryRefresh(50);

        // After TryRefresh, dirty should be cleared (it was set then consumed)
        creature.StateDirty.ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region — BroadcastEntityStates (keepalive timer)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Tick_broadcasts_state_when_keepalive_expires()
    {
        var (region, resolver, _) = MakeRegionWithSessions();
        var player = MakePlayer();
        var creature = MakeCreature();

        AddEntityDirectly(region, player, CenterPos());
        player.IsActive = true;
        AddEntityDirectly(region, creature, CenterPos(offsetX: 10));

        var (session, stub) = CreateSession();
        resolver.Register(player, session);

        // Reset keepalive — setup ticks consumed the initial timer before visibility.
        creature.NextStateRefresh = 0;
        region.Tick(1);

        var statePackets = stub.FindPackets<StationaryObjectStateResponse>();
        statePackets.Count.ShouldBeGreaterThanOrEqualTo(1);

        var state = statePackets[0];
        state.Oid.ShouldBe(creature.ObjectId);
        state.PctHealth.ShouldBe((byte)100);
    }

    [Fact]
    public void Tick_does_not_broadcast_before_keepalive_expiry()
    {
        var (region, resolver, _) = MakeRegionWithSessions();
        var player = MakePlayer();
        var creature = MakeCreature();

        AddEntityDirectly(region, player, CenterPos());
        AddEntityDirectly(region, creature, CenterPos(offsetX: 10));

        var (session, stub) = CreateSession();
        resolver.Register(player, session);

        // Initialize: first tick fires keepalive (NextStateRefresh=0 < 1)
        region.Tick(1);
        var initialCount = stub.FindPackets<StationaryObjectStateResponse>().Count;

        // Tick at t=100 — well within the 40–50s interval, should NOT broadcast again
        region.Tick(100);
        stub.FindPackets<StationaryObjectStateResponse>().Count.ShouldBe(initialCount);
    }

    [Fact]
    public void Tick_broadcasts_again_after_full_interval()
    {
        var (region, resolver, _) = MakeRegionWithSessions();
        var player = MakePlayer();
        var creature = MakeCreature();

        AddEntityDirectly(region, player, CenterPos());
        player.IsActive = true;
        AddEntityDirectly(region, creature, CenterPos(offsetX: 10));

        var (session, stub) = CreateSession();
        resolver.Register(player, session);

        // First tick fires keepalive (NextStateRefresh=0)
        region.Tick(1);
        var count1 = stub.FindPackets<StationaryObjectStateResponse>().Count;

        // Well past the max interval (50s)
        region.Tick(60_000);
        stub.FindPackets<StationaryObjectStateResponse>().Count.ShouldBeGreaterThan(count1);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region — BroadcastEntityStates (dirty flag)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Tick_broadcasts_when_dirty_flag_set()
    {
        var (region, resolver, _) = MakeRegionWithSessions();
        var player = MakePlayer();
        var creature = MakeCreature();

        AddEntityDirectly(region, player, CenterPos());
        player.IsActive = true;
        AddEntityDirectly(region, creature, CenterPos(offsetX: 10));

        var (session, stub) = CreateSession();
        resolver.Register(player, session);

        // First tick: initial keepalive fires
        region.Tick(1);
        var count1 = stub.FindPackets<StationaryObjectStateResponse>().Count;

        // Mark creature dirty (simulating a health change)
        creature.StateDirty = true;

        // Tick at t=100 — within keepalive window, but dirty flag set
        region.Tick(100);
        stub.FindPackets<StationaryObjectStateResponse>().Count.ShouldBe(count1 + 1);
    }

    [Fact]
    public void Tick_clears_dirty_flag_after_broadcast()
    {
        var (region, resolver, _) = MakeRegionWithSessions();
        var player = MakePlayer();
        var creature = MakeCreature();

        AddEntityDirectly(region, player, CenterPos());
        AddEntityDirectly(region, creature, CenterPos(offsetX: 10));

        var (session, stub) = CreateSession();
        resolver.Register(player, session);

        creature.StateDirty = true;
        region.Tick(1);

        creature.StateDirty.ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region — Players are excluded from broadcast
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Tick_does_not_broadcast_player_entity_state()
    {
        var (region, resolver, _) = MakeRegionWithSessions();
        var player1 = MakePlayer("Player1");
        var player2 = MakePlayer("Player2", charId: 2);

        AddEntityDirectly(region, player1, CenterPos());
        AddEntityDirectly(region, player2, CenterPos(offsetX: 10));

        var (session1, stub1) = CreateSession();
        var (session2, stub2) = CreateSession(sessionId: 2);
        resolver.Register(player1, session1);
        resolver.Register(player2, session2);

        // Tick far in the future to exceed any keepalive
        region.Tick(60_000);

        stub1.FindPackets<StationaryObjectStateResponse>().ShouldBeEmpty();
        stub2.FindPackets<StationaryObjectStateResponse>().ShouldBeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region — Game object keepalive
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Tick_broadcasts_game_object_keepalive()
    {
        var (region, resolver, _) = MakeRegionWithSessions();
        var player = MakePlayer();
        var go = MakeGameObject();

        AddEntityDirectly(region, player, CenterPos());
        player.IsActive = true;
        AddEntityDirectly(region, go, CenterPos(offsetX: 10));

        var (session, stub) = CreateSession();
        resolver.Register(player, session);

        go.NextStateRefresh = 0;
        region.Tick(1);

        var states = stub.FindPackets<StationaryObjectStateResponse>();
        states.Count.ShouldBeGreaterThanOrEqualTo(1);

        var goState = states.First(s => s.Oid == go.ObjectId);
        goState.PctHealth.ShouldBe((byte)100);
        goState.Flags.ShouldBe((byte)ObjectStateFlags.None);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region — Broadcast reaches all players in visibility
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Tick_broadcasts_to_all_players_in_visibility()
    {
        var (region, resolver, _) = MakeRegionWithSessions();
        var player1 = MakePlayer("P1");
        var player2 = MakePlayer("P2", charId: 2);
        var creature = MakeCreature();

        AddEntityDirectly(region, player1, CenterPos());
        player1.IsActive = true;
        AddEntityDirectly(region, player2, CenterPos(offsetX: 5));
        player2.IsActive = true;
        AddEntityDirectly(region, creature, CenterPos(offsetX: 10));

        var (session1, stub1) = CreateSession();
        var (session2, stub2) = CreateSession(sessionId: 2);
        resolver.Register(player1, session1);
        resolver.Register(player2, session2);

        creature.NextStateRefresh = 0;
        region.Tick(1);

        stub1.FindPackets<StationaryObjectStateResponse>()
            .ShouldContain(s => s.Oid == creature.ObjectId);
        stub2.FindPackets<StationaryObjectStateResponse>()
            .ShouldContain(s => s.Oid == creature.ObjectId);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region — No broadcast when no players watching
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Tick_skips_broadcast_when_no_players_in_visibility()
    {
        var (region, resolver, _) = MakeRegionWithSessions();
        var creature = MakeCreature();

        var player = MakePlayer();
        AddEntityDirectly(region, player, CenterPos());

        // Creature in a far-away cell — outside visibility range
        var farPos = new WorldPosition(1, 50 * 4096, 50 * 4096, 0, 0, TestZoneId);
        AddEntityDirectly(region, creature, farPos);

        var (session, stub) = CreateSession();
        resolver.Register(player, session);

        creature.Visibility.PlayerCount.ShouldBe(0);
        region.Tick(1);

        stub.FindPackets<StationaryObjectStateResponse>()
            .ShouldNotContain(s => s.Oid == creature.ObjectId);
    }

    // ═══════════════════════════════════════════════════════════════════
    // ObjectStateFlags — sanity checks
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ObjectStateFlags_Moving_is_bit_0()
        => ((byte)ObjectStateFlags.Moving).ShouldBe((byte)0x01);

    [Fact]
    public void ObjectStateFlags_LookingAt_is_bit_1()
        => ((byte)ObjectStateFlags.LookingAt).ShouldBe((byte)0x02);

    [Fact]
    public void ObjectStateFlags_Recall_is_0x28()
        => ((byte)ObjectStateFlags.Recall).ShouldBe((byte)0x28);

    // ═══════════════════════════════════════════════════════════════════
    // WorldEntity — StateRefreshInterval is randomized
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void StateRefreshInterval_is_in_40_to_50_second_range()
    {
        var intervals = Enumerable.Range(0, 20)
            .Select(_ => MakeCreature())
            .Select(c => c.StateRefreshInterval)
            .ToList();

        intervals.ShouldAllBe(i => i >= 40_000 && i < 50_000);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════

    private const ushort TestZoneId = 100;

    private static readonly ILogger<Region> Logger =
        NullLoggerFactory.Instance.CreateLogger<Region>();

    private static readonly IEntityFactory StubFactory = new StubEntityFactory();
    private static readonly WorldServerMetrics Metrics = new();
    private static readonly IRegionEventDispatcher StubDispatcher = new StubEventDispatcher();

    private sealed class StubEventDispatcher : IRegionEventDispatcher
    {
        public void Dispatch<TEvent>(TEvent @event) { }
    }

    private static IRegionEventDispatcher MakeDispatcher(ISessionResolver<PlayerEntity> resolver)
        => new DelegatingDispatcher(new VisibilityHandler(resolver));

    private sealed class DelegatingDispatcher(VisibilityHandler handler) : IRegionEventDispatcher
    {
        public void Dispatch<TEvent>(TEvent @event)
        {
            switch (@event)
            {
                case EntityBecameVisible v: handler.Handle(v); break;
                case EntityStateChanged s: handler.Handle(s); break;
            }
        }
    }

    private static WorldPosition CenterPos(int offsetX = 0, int offsetY = 0)
        => new(1, 5 * 4096 + offsetX, 5 * 4096 + offsetY, 0, 0, TestZoneId);

    private static WorldPosition MakePosition(int x = 20480, int y = 20480, int z = 0,
        ushort heading = 0, ushort zone = TestZoneId)
        => new(1, x, y, z, heading, zone);

    private static ZoneInfo MakeZoneInfo(ushort zoneId, int offX = 0, int offY = 0)
        => new() { ZoneId = zoneId, OffX = offX, OffY = offY, Region = 1, Name = "TestZone" };

    private static CreatureEntity MakeCreature(string name = "Mob", ushort id = 0)
        => new(id, new CreatureProto { Entry = 1, Name = name }, 500);

    private static GameObjectEntity MakeGameObject(string name = "Chest", ushort id = 0)
        => new(id, new GameObjectSpawnDescriptor { Entry = 100, RegionId = 1, ZoneId = TestZoneId, Position = default }, name);

    private static PlayerEntity MakePlayer(string name = "Player", ushort id = 0, uint charId = 1)
        => new(id, new Character { CharacterId = charId, Name = name }, 1000);

    private static void AddEntityDirectly(Region region, WorldEntity entity, WorldPosition pos)
    {
        region.EnqueueAdd(entity, pos);
        region.Tick(0);
    }

    private static (GameSession Session, StubConnectionContext Stub) CreateSession(ushort sessionId = 1)
    {
        var stub = new StubConnectionContext();
        var session = new GameSession(sessionId, stub);
        session.MoveToPlaying();
        return (session, stub);
    }

    private static (Region Region, RecordingSessionResolver Resolver, IGameDataStore Data)
        MakeRegionWithSessions()
    {
        var resolver = new RecordingSessionResolver();
        var data = new StubGameDataStoreWithZone(TestZoneId);
        var region = new Region(1, MakeDispatcher(resolver), StubFactory, data, Logger, Metrics);
        return (region, resolver, data);
    }

    // ── Stubs ────────────────────────────────────────────────────────

    private sealed class StubEntityFactory : IEntityFactory
    {
        public CreatureEntity CreateCreature(SpawnDescriptor d)
            => new(0, new CreatureProto { Entry = d.Entry, Name = "stub" }, 100);
        public GameObjectEntity CreateGameObject(GameObjectSpawnDescriptor d)
            => new(0, d);
    }

    private sealed class RecordingSessionResolver : ISessionResolver<PlayerEntity>
    {
        private readonly Dictionary<uint, GameSession> _sessions = new();

        public void Register(PlayerEntity player, GameSession session)
            => _sessions[player.CharacterId] = session;

        public GameSession? GetSession(PlayerEntity player)
            => _sessions.GetValueOrDefault(player.CharacterId);
    }

    private sealed class StubGameDataStoreWithZone : IGameDataStore
    {
        public StubGameDataStoreWithZone(ushort zoneId)
        {
            var zoneInfo = new ZoneInfo
            {
                ZoneId = zoneId,
                OffX = 5,
                OffY = 5,
                Region = 1,
                Name = "TestZone",
            };
            Zones = new ZoneData(
                new Dictionary<ushort, ZoneInfo> { [zoneId] = zoneInfo }.ToFrozenDictionary(),
                FrozenDictionary<uint, ZoneJump>.Empty);
        }

        public ClassData Classes => new(
            FrozenDictionary<Class, ClassInfo>.Empty,
            FrozenDictionary<Class, List<ClassInfoItem>>.Empty);
        public ItemData Items => new(
            FrozenDictionary<uint, ItemDefinition>.Empty,
            FrozenDictionary<uint, ItemSetDefinition>.Empty);
        public CreatureData Creatures => new(
            FrozenDictionary<uint, CreatureProto>.Empty,
            FrozenDictionary<uint, CreatureSpawn>.Empty,
            FrozenDictionary<uint, ImmutableArray<CreatureItem>>.Empty);
        public ZoneData Zones { get; }
        public CareerStatData CareerStats => CareerStatData.Empty;
        public AbilityData Abilities => AbilityData.Empty;
        public SpawnData Spawns => SpawnData.Empty;
    }

    private sealed class StubConnectionContext : Core.Infrastructure.Network.IConnectionContext
    {
        private readonly List<(byte Opcode, object Packet)> _sent = [];

        public int PacketCount => _sent.Count;

        public T? FindPacket<T>() where T : class =>
            _sent.FirstOrDefault(p => p.Packet is T).Packet as T;

        public List<T> FindPackets<T>() where T : class =>
            _sent.Where(p => p.Packet is T).Select(p => (T)p.Packet).ToList();

        public string? RemoteAddress => "127.0.0.1:12345";

        public Core.Infrastructure.Network.IPacketFramer PacketFramer =>
            throw new NotImplementedException("Not needed for unit tests");

        public void SendResponse<T>(byte opcode, T response)
            => _sent.Add((opcode, response!));

        public void Disconnect(string reason, bool flush = false) { }

        public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();

        public void OnDispatchError(byte opcode, Exception exception) { }
    }
}
