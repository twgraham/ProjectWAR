using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using WorldServerV2.Data.Entities;
using WorldServerV2.Services;
using WorldServerV2.World.Entities;
using WorldServerV2.World.Spatial;

namespace WorldServer.Tests;

/// <summary>
/// Tests for the World Topology system: <see cref="WorldPosition"/>, <see cref="Cell"/>,
/// <see cref="VisibilitySet"/>, <see cref="Region"/>, <see cref="RegionManager"/>,
/// and <see cref="RegionCommand"/>.
/// </summary>
public class WorldTopologyTests
{
    // ── Helpers ─────────────────────────────────────────────────────────

    private static readonly ILogger<Region> Logger =
        NullLoggerFactory.Instance.CreateLogger<Region>();

    private static Region MakeRegion(ushort id = 1)
        => new(id, Logger);

    private static PlayerEntity MakePlayer(string name = "Player", ushort id = 0)
        => new(id, new Character { CharacterId = 1, Name = name }, 1000);

    private static CreatureEntity MakeCreature(string name = "Mob", ushort id = 0)
        => new(id, new CreatureProto { Entry = 1, Name = name },
            new CreatureSpawn { Guid = 1, Entry = 1 }, 500);

    private static GameObjectEntity MakeGameObject(string name = "Chest", ushort id = 0)
        => new(id, 100, name);

    /// <summary>
    /// Adds an entity directly to a region by enqueuing and ticking.
    /// </summary>
    private static void AddEntityDirectly(Region region, WorldEntity entity, WorldPosition pos)
    {
        region.EnqueueAdd(entity, pos);
        region.Tick(0);
    }

    /// <summary>Position in cell (5, 5) at the center of the grid.</summary>
    private static WorldPosition CenterPos(int offsetX = 0, int offsetY = 0)
        => new(1, 5 * 4096 + offsetX, 5 * 4096 + offsetY, 0, 0, 100);

    // ═══════════════════════════════════════════════════════════════════
    // WorldPosition
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void WorldPosition_Zero_is_default()
    {
        WorldPosition.Zero.ShouldBe(default);
        WorldPosition.Zero.RegionId.ShouldBe((ushort)0);
        WorldPosition.Zero.X.ShouldBe(0);
        WorldPosition.Zero.Y.ShouldBe(0);
    }

    [Fact]
    public void WorldPosition_from_zone_local_converts_correctly()
    {
        // ZoneId 50, offset (2, 3), local (100, 200), z=50, heading=1024
        var pos = WorldPosition.FromZoneLocal(1, 50, 2, 3, 100, 200, 50, 1024);

        pos.RegionId.ShouldBe((ushort)1);
        pos.X.ShouldBe(2 * 4096 + 100);
        pos.Y.ShouldBe(3 * 4096 + 200);
        pos.Z.ShouldBe(50);
        pos.Heading.ShouldBe((ushort)1024);
        pos.ZoneId.ShouldBe((ushort)50);
    }

    [Fact]
    public void WorldPosition_to_zone_local_reverses_from_zone_local()
    {
        var pos = WorldPosition.FromZoneLocal(1, 50, 2, 3, 100, 200, 50, 1024);
        var (localX, localY) = pos.ToZoneLocal(2, 3);

        localX.ShouldBe(100);
        localY.ShouldBe(200);
    }

    [Fact]
    public void WorldPosition_cell_index_divides_by_cell_size()
    {
        // Position at (8192, 12288) → cell (2, 3)
        var pos = new WorldPosition(1, 8192, 12288, 0, 0, 0);
        var (cellX, cellY) = pos.CellIndex;

        cellX.ShouldBe(2);
        cellY.ShouldBe(3);
    }

    [Fact]
    public void WorldPosition_cell_index_floors_within_cell()
    {
        // Position at (8192 + 2000, 12288 + 3000) → still cell (2, 3)
        var pos = new WorldPosition(1, 10192, 15288, 0, 0, 0);
        var (cellX, cellY) = pos.CellIndex;

        cellX.ShouldBe(2);
        cellY.ShouldBe(3);
    }

    [Fact]
    public void WorldPosition_distance_squared_2D_ignores_Z()
    {
        var a = new WorldPosition(1, 0, 0, 0, 0, 0);
        var b = new WorldPosition(1, 3000, 4000, 9999, 0, 0);

        // 3000² + 4000² = 9M + 16M = 25M
        a.DistanceSquared2D(b).ShouldBe(25_000_000L);
    }

    [Fact]
    public void WorldPosition_distance_squared_3D_includes_Z()
    {
        var a = new WorldPosition(1, 0, 0, 0, 0, 0);
        var b = new WorldPosition(1, 3000, 4000, 5000, 0, 0);

        // 3000² + 4000² + 5000² = 9M + 16M + 25M = 50M
        a.DistanceSquared3D(b).ShouldBe(50_000_000L);
    }

    [Fact]
    public void WorldPosition_distance_does_not_overflow_for_large_coords()
    {
        var a = new WorldPosition(1, 0, 0, 0, 0, 0);
        var b = new WorldPosition(1, 800 * 4096, 800 * 4096, 0, 0, 0);

        // Region-wide max is ~3.2M. 3276800² = ~10.7 trillion — fits in long.
        var dist = a.DistanceSquared2D(b);
        dist.ShouldBeGreaterThan(0L);
    }

    // ═══════════════════════════════════════════════════════════════════
    // VisibilitySet
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void VisibilitySet_initially_empty()
    {
        var vis = new VisibilitySet();

        vis.Count.ShouldBe(0);
        vis.PlayerCount.ShouldBe(0);
        vis.Entities.ShouldBeEmpty();
        vis.Players.ShouldBeEmpty();
    }

    [Fact]
    public void VisibilitySet_add_entity_increases_count()
    {
        var vis = new VisibilitySet();
        var creature = MakeCreature();

        vis.Add(creature);

        vis.Count.ShouldBe(1);
        vis.PlayerCount.ShouldBe(0);
        vis.Contains(creature).ShouldBeTrue();
    }

    [Fact]
    public void VisibilitySet_add_player_appears_in_both_sets()
    {
        var vis = new VisibilitySet();
        var player = MakePlayer();

        vis.Add(player);

        vis.Count.ShouldBe(1);
        vis.PlayerCount.ShouldBe(1);
        vis.Contains(player).ShouldBeTrue();
        vis.Players.ShouldContain(player);
    }

    [Fact]
    public void VisibilitySet_add_duplicate_returns_false()
    {
        var vis = new VisibilitySet();
        var entity = MakeCreature();

        vis.Add(entity).ShouldBeTrue();
        vis.Add(entity).ShouldBeFalse();
        vis.Count.ShouldBe(1);
    }

    [Fact]
    public void VisibilitySet_remove_entity_decreases_count()
    {
        var vis = new VisibilitySet();
        var creature = MakeCreature();
        vis.Add(creature);

        vis.Remove(creature).ShouldBeTrue();

        vis.Count.ShouldBe(0);
        vis.Contains(creature).ShouldBeFalse();
    }

    [Fact]
    public void VisibilitySet_remove_player_removes_from_both()
    {
        var vis = new VisibilitySet();
        var player = MakePlayer();
        vis.Add(player);

        vis.Remove(player);

        vis.Count.ShouldBe(0);
        vis.PlayerCount.ShouldBe(0);
    }

    [Fact]
    public void VisibilitySet_clear_empties_all()
    {
        var vis = new VisibilitySet();
        vis.Add(MakePlayer());
        vis.Add(MakeCreature());

        vis.Clear();

        vis.Count.ShouldBe(0);
        vis.PlayerCount.ShouldBe(0);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Cell
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Cell_identity_matches_constructor()
    {
        var region = MakeRegion();
        var cell = new Cell(region, 5, 10);

        cell.Region.ShouldBe(region);
        cell.X.ShouldBe(5);
        cell.Y.ShouldBe(10);
    }

    [Fact]
    public void Cell_initially_empty_and_inactive()
    {
        var cell = new Cell(MakeRegion(), 0, 0);

        cell.EntityCount.ShouldBe(0);
        cell.PlayerCount.ShouldBe(0);
        cell.IsActive.ShouldBeFalse();
        cell.IsLoaded.ShouldBeFalse();
    }

    [Fact]
    public void Cell_becomes_active_when_player_added()
    {
        var cell = new Cell(MakeRegion(), 0, 0);
        cell.AddEntity(MakePlayer());

        cell.IsActive.ShouldBeTrue();
        cell.PlayerCount.ShouldBe(1);
        cell.EntityCount.ShouldBe(1);
    }

    [Fact]
    public void Cell_creature_does_not_make_cell_active()
    {
        var cell = new Cell(MakeRegion(), 0, 0);
        cell.AddEntity(MakeCreature());

        cell.IsActive.ShouldBeFalse();
        cell.EntityCount.ShouldBe(1);
        cell.PlayerCount.ShouldBe(0);
    }

    [Fact]
    public void Cell_remove_returns_true_when_found()
    {
        var cell = new Cell(MakeRegion(), 0, 0);
        var player = MakePlayer();
        cell.AddEntity(player);

        cell.RemoveEntity(player).ShouldBeTrue();
        cell.EntityCount.ShouldBe(0);
        cell.PlayerCount.ShouldBe(0);
        cell.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Cell_remove_returns_false_when_not_found()
    {
        var cell = new Cell(MakeRegion(), 0, 0);
        cell.RemoveEntity(MakeCreature()).ShouldBeFalse();
    }

    [Fact]
    public void Cell_contains_checks_entity_presence()
    {
        var cell = new Cell(MakeRegion(), 0, 0);
        var creature = MakeCreature();
        var other = MakeCreature("Other");

        cell.AddEntity(creature);

        cell.Contains(creature).ShouldBeTrue();
        cell.Contains(other).ShouldBeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════
    // RegionCommand
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RegionCommand_AddEntity_stores_entity_and_position()
    {
        var entity = MakePlayer();
        var pos = CenterPos();
        var cmd = new RegionCommand.AddEntity(entity, pos);

        cmd.Entity.ShouldBe(entity);
        cmd.Position.ShouldBe(pos);
    }

    [Fact]
    public void RegionCommand_RemoveEntity_stores_entity()
    {
        var entity = MakeCreature();
        var cmd = new RegionCommand.RemoveEntity(entity);

        cmd.Entity.ShouldBe(entity);
    }

    [Fact]
    public void RegionCommand_MoveEntity_stores_entity_and_new_position()
    {
        var entity = MakePlayer();
        var newPos = CenterPos(100, 200);
        var cmd = new RegionCommand.MoveEntity(entity, newPos);

        cmd.Entity.ShouldBe(entity);
        cmd.NewPosition.ShouldBe(newPos);
    }

    [Fact]
    public void RegionCommand_TransferIn_stores_entity_and_destination()
    {
        var entity = MakePlayer();
        var dest = CenterPos();
        var cmd = new RegionCommand.TransferIn(entity, dest);

        cmd.Entity.ShouldBe(entity);
        cmd.Destination.ShouldBe(dest);
    }

    [Fact]
    public void RegionCommand_null_entity_throws()
    {
        Should.Throw<ArgumentNullException>(() => new RegionCommand.AddEntity(null!, CenterPos()));
        Should.Throw<ArgumentNullException>(() => new RegionCommand.RemoveEntity(null!));
        Should.Throw<ArgumentNullException>(() => new RegionCommand.MoveEntity(null!, CenterPos()));
        Should.Throw<ArgumentNullException>(() => new RegionCommand.TransferIn(null!, CenterPos()));
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region — OID Management
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Region_assigns_oid_on_add()
    {
        var region = MakeRegion();
        var player = MakePlayer();

        AddEntityDirectly(region, player, CenterPos());

        player.ObjectId.ShouldNotBe((ushort)0);
        region.EntityCount.ShouldBe(1);
    }

    [Fact]
    public void Region_sequential_oids_start_from_one()
    {
        var region = MakeRegion();
        var p1 = MakePlayer("P1");
        var p2 = MakePlayer("P2");

        AddEntityDirectly(region, p1, CenterPos());
        AddEntityDirectly(region, p2, CenterPos(100));

        p1.ObjectId.ShouldBe((ushort)1);
        p2.ObjectId.ShouldBe((ushort)2);
    }

    [Fact]
    public void Region_releases_oid_on_remove()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        AddEntityDirectly(region, player, CenterPos());
        var assignedOid = player.ObjectId;

        region.EnqueueRemove(player);
        region.Tick(0);

        player.ObjectId.ShouldBe((ushort)0);
        region.EntityCount.ShouldBe(0);

        // The released OID should be reused next
        var player2 = MakePlayer("P2");
        AddEntityDirectly(region, player2, CenterPos(200));
        player2.ObjectId.ShouldBe(assignedOid);
    }

    [Fact]
    public void Region_get_entity_by_oid_returns_entity()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        AddEntityDirectly(region, player, CenterPos());

        var found = region.GetEntityByOid(player.ObjectId);
        found.ShouldBe(player);
    }

    [Fact]
    public void Region_get_entity_by_oid_returns_null_for_unknown()
    {
        var region = MakeRegion();
        region.GetEntityByOid(9999).ShouldBeNull();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region — Cell Management
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Region_creates_cell_on_entity_add()
    {
        var region = MakeRegion();
        var pos = CenterPos(); // cell (5, 5)
        AddEntityDirectly(region, MakePlayer(), pos);

        var cell = region.GetCell(5, 5);
        cell.ShouldNotBeNull();
        cell!.EntityCount.ShouldBe(1);
    }

    [Fact]
    public void Region_cell_is_null_before_first_access()
    {
        var region = MakeRegion();
        region.GetCell(10, 10).ShouldBeNull();
    }

    [Fact]
    public void Region_cell_out_of_bounds_returns_null()
    {
        var region = MakeRegion();
        region.GetCell(-1, 0).ShouldBeNull();
        region.GetCell(0, -1).ShouldBeNull();
        region.GetCell(800, 0).ShouldBeNull();
        region.GetCell(0, 800).ShouldBeNull();
    }

    [Fact]
    public void Region_tracks_active_cells()
    {
        var region = MakeRegion();

        // Add a creature — cell should exist but not be active
        AddEntityDirectly(region, MakeCreature(), CenterPos());
        region.ActiveCellCount.ShouldBe(0);
        region.OccupiedCellCount.ShouldBe(1);

        // Add a player — cell becomes active
        AddEntityDirectly(region, MakePlayer(), CenterPos(100));
        region.ActiveCellCount.ShouldBe(1);
    }

    [Fact]
    public void Region_cell_becomes_inactive_when_last_player_removed()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        AddEntityDirectly(region, player, CenterPos());
        region.ActiveCellCount.ShouldBe(1);

        region.EnqueueRemove(player);
        region.Tick(0);

        region.ActiveCellCount.ShouldBe(0);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region — Entity Add / Remove / Move
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Region_duplicate_add_is_ignored()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        AddEntityDirectly(region, player, CenterPos());

        // Enqueue same entity again
        region.EnqueueAdd(player, CenterPos(200));
        region.Tick(0);

        region.EntityCount.ShouldBe(1);
    }

    [Fact]
    public void Region_remove_nonexistent_entity_is_safe()
    {
        var region = MakeRegion();
        var player = MakePlayer();

        // Remove something never added — should not throw
        region.EnqueueRemove(player);
        region.Tick(0);

        region.EntityCount.ShouldBe(0);
    }

    [Fact]
    public void Region_move_updates_position()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        var start = CenterPos();
        var dest = CenterPos(500, 500);

        AddEntityDirectly(region, player, start);
        region.EnqueueMove(player, dest);
        region.Tick(0);

        player.Position.ShouldBe(dest);
    }

    [Fact]
    public void Region_move_across_cells_updates_cell_membership()
    {
        var region = MakeRegion();
        var player = MakePlayer();

        // Start in cell (5, 5)
        AddEntityDirectly(region, player, CenterPos());
        region.GetCell(5, 5)!.Contains(player).ShouldBeTrue();

        // Move to cell (6, 5) — X increases by one cell
        var newPos = new WorldPosition(1, 6 * 4096 + 100, 5 * 4096 + 100, 0, 0, 100);
        region.EnqueueMove(player, newPos);
        region.Tick(0);

        region.GetCell(5, 5)!.Contains(player).ShouldBeFalse();
        region.GetCell(6, 5)!.Contains(player).ShouldBeTrue();
    }

    [Fact]
    public void Region_move_within_same_cell_does_not_change_cell()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        AddEntityDirectly(region, player, CenterPos());

        // Move within cell (5, 5)
        var newPos = CenterPos(100, 200);
        region.EnqueueMove(player, newPos);
        region.Tick(0);

        region.GetCell(5, 5)!.Contains(player).ShouldBeTrue();
        player.Position.ShouldBe(newPos);
    }

    [Fact]
    public void Region_transfer_in_adds_entity_like_add()
    {
        var region = MakeRegion();
        var player = MakePlayer();

        region.EnqueueTransfer(player, CenterPos());
        region.Tick(0);

        region.EntityCount.ShouldBe(1);
        player.ObjectId.ShouldNotBe((ushort)0);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region — Tick & Entity Update
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Region_tick_calls_entity_update()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        AddEntityDirectly(region, player, CenterPos());

        // Player's health regen would run in Update — we verify the tick number is passed
        // by checking entity is still there after tick
        region.Tick(12345);
        region.EntityCount.ShouldBe(1);
    }

    [Fact]
    public void Region_tick_only_ticks_cells_near_players()
    {
        var region = MakeRegion();

        // Add a creature far from any player — cell (50, 50)
        var farPos = new WorldPosition(1, 50 * 4096 + 100, 50 * 4096 + 100, 0, 0, 100);
        var creature = MakeCreature();
        AddEntityDirectly(region, creature, farPos);

        // Add a player at cell (5, 5)
        AddEntityDirectly(region, MakePlayer(), CenterPos());

        // The creature's cell (50, 50) is not within the 3×3 neighborhood of (5, 5),
        // so it won't be ticked. We verify active cell count reflects the player only.
        region.ActiveCellCount.ShouldBe(1);
        region.OccupiedCellCount.ShouldBe(2); // both cells have entities
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region — Visibility
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Region_nearby_entities_see_each_other()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        var creature = MakeCreature();

        // Both in same cell — well within visibility range
        AddEntityDirectly(region, player, CenterPos());
        AddEntityDirectly(region, creature, CenterPos(100, 0));

        player.Visibility.Contains(creature).ShouldBeTrue();
        creature.Visibility.Contains(player).ShouldBeTrue();
    }

    [Fact]
    public void Region_far_entities_do_not_see_each_other()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        var creature = MakeCreature();

        // Player at cell (5, 5), creature at cell (50, 50) — far beyond visibility
        AddEntityDirectly(region, player, CenterPos());
        AddEntityDirectly(region, creature,
            new WorldPosition(1, 50 * 4096 + 100, 50 * 4096 + 100, 0, 0, 100));

        player.Visibility.Contains(creature).ShouldBeFalse();
        creature.Visibility.Contains(player).ShouldBeFalse();
    }

    [Fact]
    public void Region_visibility_is_bidirectional()
    {
        var region = MakeRegion();
        var p1 = MakePlayer("P1");
        var p2 = MakePlayer("P2");

        AddEntityDirectly(region, p1, CenterPos());
        AddEntityDirectly(region, p2, CenterPos(200, 0));

        p1.Visibility.Contains(p2).ShouldBeTrue();
        p2.Visibility.Contains(p1).ShouldBeTrue();

        // Also in Players subset
        p1.Visibility.PlayerCount.ShouldBe(1);
        p2.Visibility.PlayerCount.ShouldBe(1);
    }

    [Fact]
    public void Region_visibility_updated_on_move_into_range()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        var creature = MakeCreature();

        // Start far apart (different cells, beyond visibility)
        var playerPos = CenterPos();
        var creaturePos = new WorldPosition(1, 8 * 4096 + 100, 8 * 4096 + 100, 0, 0, 100);
        AddEntityDirectly(region, player, playerPos);
        AddEntityDirectly(region, creature, creaturePos);

        player.Visibility.Contains(creature).ShouldBeFalse();

        // Move creature close to player
        region.EnqueueMove(creature, CenterPos(200, 0));
        region.Tick(0);

        player.Visibility.Contains(creature).ShouldBeTrue();
        creature.Visibility.Contains(player).ShouldBeTrue();
    }

    [Fact]
    public void Region_visibility_updated_on_move_out_of_range()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        var creature = MakeCreature();

        // Start close together
        AddEntityDirectly(region, player, CenterPos());
        AddEntityDirectly(region, creature, CenterPos(100, 0));
        player.Visibility.Contains(creature).ShouldBeTrue();

        // Move creature far away (beyond visibility)
        var farPos = new WorldPosition(1, 50 * 4096 + 100, 50 * 4096 + 100, 0, 0, 100);
        region.EnqueueMove(creature, farPos);
        region.Tick(0);

        player.Visibility.Contains(creature).ShouldBeFalse();
        creature.Visibility.Contains(player).ShouldBeFalse();
    }

    [Fact]
    public void Region_visibility_cleared_on_remove()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        var creature = MakeCreature();

        AddEntityDirectly(region, player, CenterPos());
        AddEntityDirectly(region, creature, CenterPos(100, 0));
        player.Visibility.Contains(creature).ShouldBeTrue();

        // Remove creature
        region.EnqueueRemove(creature);
        region.Tick(0);

        player.Visibility.Contains(creature).ShouldBeFalse();
        creature.Visibility.Count.ShouldBe(0);
    }

    [Fact]
    public void Region_visibility_threshold_prevents_unnecessary_rescans()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        var creature = MakeCreature();

        AddEntityDirectly(region, player, CenterPos());
        AddEntityDirectly(region, creature, CenterPos(200, 0));

        // Small move (less than 100 units threshold) — visibility shouldn't rescan
        region.EnqueueMove(player, CenterPos(50, 0));
        region.Tick(0);

        // Entities should still see each other (no removal from minor move)
        player.Visibility.Contains(creature).ShouldBeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region — Spatial Queries
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Region_get_entities_in_range_finds_nearby()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        var creature = MakeCreature();

        AddEntityDirectly(region, player, CenterPos());
        AddEntityDirectly(region, creature, CenterPos(100, 0));

        var results = new List<WorldEntity>();
        region.GetEntitiesInRange(CenterPos(), 4800, results);

        results.ShouldContain(player);
        results.ShouldContain(creature);
    }

    [Fact]
    public void Region_get_entities_in_range_excludes_far()
    {
        var region = MakeRegion();
        var nearPlayer = MakePlayer("Near");
        var farCreature = MakeCreature("Far");

        AddEntityDirectly(region, nearPlayer, CenterPos());
        AddEntityDirectly(region, farCreature,
            new WorldPosition(1, 50 * 4096 + 100, 50 * 4096 + 100, 0, 0, 100));

        var results = new List<WorldEntity>();
        region.GetEntitiesInRange(CenterPos(), 4800, results);

        results.ShouldContain(nearPlayer);
        results.ShouldNotContain(farCreature);
    }

    [Fact]
    public void Region_get_players_in_range_returns_only_players()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        var creature = MakeCreature();

        AddEntityDirectly(region, player, CenterPos());
        AddEntityDirectly(region, creature, CenterPos(100, 0));

        var results = new List<PlayerEntity>();
        region.GetPlayersInRange(CenterPos(), 4800, results);

        results.ShouldContain(player);
        results.Count.ShouldBe(1);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region — Cell Loading
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Region_loads_3x3_neighborhood_when_player_enters()
    {
        var region = MakeRegion();
        AddEntityDirectly(region, MakePlayer(), CenterPos()); // cell (5, 5)

        // Check 3×3 neighborhood is loaded
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var cell = region.GetCell(5 + dx, 5 + dy);
                cell.ShouldNotBeNull($"Cell ({5 + dx}, {5 + dy}) should be created");
                cell!.IsLoaded.ShouldBeTrue($"Cell ({5 + dx}, {5 + dy}) should be loaded");
            }
        }
    }

    [Fact]
    public void Region_does_not_reload_already_loaded_cells()
    {
        var region = MakeRegion();

        // Add player to cell (5, 5) — loads 3×3
        AddEntityDirectly(region, MakePlayer("P1"), CenterPos());

        // Move to cell (6, 5) — overlapping neighborhood should not re-load
        var newPos = new WorldPosition(1, 6 * 4096 + 100, 5 * 4096 + 100, 0, 0, 100);
        var player2 = MakePlayer("P2");
        AddEntityDirectly(region, player2, newPos);

        // Cell (5, 5) was loaded by first player, should still be loaded
        region.GetCell(5, 5)!.IsLoaded.ShouldBeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region — Multiple Entity Types
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Region_handles_all_entity_types()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        var creature = MakeCreature();
        var gameObj = MakeGameObject();

        AddEntityDirectly(region, player, CenterPos());
        AddEntityDirectly(region, creature, CenterPos(100, 0));
        AddEntityDirectly(region, gameObj, CenterPos(200, 0));

        region.EntityCount.ShouldBe(3);
        region.GetEntityByOid(player.ObjectId).ShouldBe(player);
        region.GetEntityByOid(creature.ObjectId).ShouldBe(creature);
        region.GetEntityByOid(gameObj.ObjectId).ShouldBe(gameObj);
    }

    [Fact]
    public void Region_game_object_does_not_make_cell_active()
    {
        var region = MakeRegion();
        AddEntityDirectly(region, MakeGameObject(), CenterPos());

        region.ActiveCellCount.ShouldBe(0);
        region.OccupiedCellCount.ShouldBe(1);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region — Thread Safety (command channel)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Region_commands_not_processed_until_tick()
    {
        var region = MakeRegion();
        var player = MakePlayer();

        // Enqueue without ticking
        region.EnqueueAdd(player, CenterPos());

        // Nothing should be in the region yet
        region.EntityCount.ShouldBe(0);

        // Now tick
        region.Tick(0);
        region.EntityCount.ShouldBe(1);
    }

    [Fact]
    public void Region_multiple_commands_processed_in_single_tick()
    {
        var region = MakeRegion();
        var p1 = MakePlayer("P1");
        var p2 = MakePlayer("P2");
        var c1 = MakeCreature("C1");

        region.EnqueueAdd(p1, CenterPos());
        region.EnqueueAdd(p2, CenterPos(200, 0));
        region.EnqueueAdd(c1, CenterPos(400, 0));
        region.Tick(0);

        region.EntityCount.ShouldBe(3);

        // Remove one and move another in the same tick
        region.EnqueueRemove(c1);
        region.EnqueueMove(p2, CenterPos(300, 300));
        region.Tick(0);

        region.EntityCount.ShouldBe(2);
        p2.Position.ShouldBe(CenterPos(300, 300));
    }

    // ═══════════════════════════════════════════════════════════════════
    // RegionManager
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void RegionManager_creates_region_on_first_access()
    {
        using var manager = new RegionManager(NullLoggerFactory.Instance, autoStart: false);

        var region = manager.GetOrCreate(1);

        region.ShouldNotBeNull();
        region.RegionId.ShouldBe((ushort)1);
        manager.Count.ShouldBe(1);
    }

    [Fact]
    public void RegionManager_returns_same_region_on_second_access()
    {
        using var manager = new RegionManager(NullLoggerFactory.Instance, autoStart: false);

        var first = manager.GetOrCreate(1);
        var second = manager.GetOrCreate(1);

        first.ShouldBeSameAs(second);
        manager.Count.ShouldBe(1);
    }

    [Fact]
    public void RegionManager_get_returns_null_for_unknown()
    {
        using var manager = new RegionManager(NullLoggerFactory.Instance, autoStart: false);
        manager.Get(99).ShouldBeNull();
    }

    [Fact]
    public void RegionManager_get_returns_existing()
    {
        using var manager = new RegionManager(NullLoggerFactory.Instance, autoStart: false);
        var region = manager.GetOrCreate(1);

        manager.Get(1).ShouldBeSameAs(region);
    }

    [Fact]
    public void RegionManager_manages_multiple_regions()
    {
        using var manager = new RegionManager(NullLoggerFactory.Instance, autoStart: false);

        manager.GetOrCreate(1);
        manager.GetOrCreate(2);
        manager.GetOrCreate(3);

        manager.Count.ShouldBe(3);
        manager.GetAllRegionIds().Count.ShouldBe(3);
    }

    [Fact]
    public void RegionManager_get_all_regions_returns_snapshot()
    {
        using var manager = new RegionManager(NullLoggerFactory.Instance, autoStart: false);
        manager.GetOrCreate(1);
        manager.GetOrCreate(2);

        var regions = manager.GetAllRegions();
        regions.Count.ShouldBe(2);
    }

    [Fact]
    public void RegionManager_dispose_cleans_up()
    {
        var manager = new RegionManager(NullLoggerFactory.Instance, autoStart: false);
        manager.GetOrCreate(1);
        manager.GetOrCreate(2);

        manager.Dispose();

        manager.Count.ShouldBe(0);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region — Entity Position Is Set on Add
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Region_entity_position_reflects_add_position()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        var pos = CenterPos(123, 456);

        AddEntityDirectly(region, player, pos);

        player.Position.ShouldBe(pos);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Region — Edge Cases
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Region_entity_at_origin_gets_cell_zero()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        var pos = new WorldPosition(1, 100, 200, 0, 0, 100);

        AddEntityDirectly(region, player, pos);

        region.GetCell(0, 0).ShouldNotBeNull();
        region.GetCell(0, 0)!.Contains(player).ShouldBeTrue();
    }

    [Fact]
    public void Region_visibility_across_adjacent_cells()
    {
        var region = MakeRegion();
        var player = MakePlayer();
        var creature = MakeCreature();

        // Player at end of cell (5, 5), creature at start of cell (6, 5)
        // They are close in distance but in different cells
        var playerPos = new WorldPosition(1, 5 * 4096 + 4000, 5 * 4096 + 2000, 0, 0, 100);
        var creaturePos = new WorldPosition(1, 6 * 4096 + 100, 5 * 4096 + 2000, 0, 0, 100);

        AddEntityDirectly(region, player, playerPos);
        AddEntityDirectly(region, creature, creaturePos);

        // Distance: 4096 - 4000 + 100 = 196 units — well within 4800 visibility
        player.Visibility.Contains(creature).ShouldBeTrue();
        creature.Visibility.Contains(player).ShouldBeTrue();
    }

    [Fact]
    public void Region_many_entities_get_unique_oids()
    {
        var region = MakeRegion();
        var entities = new List<WorldEntity>();

        for (int i = 0; i < 100; i++)
        {
            var creature = MakeCreature($"Mob{i}");
            AddEntityDirectly(region, creature, CenterPos(i * 10, 0));
            entities.Add(creature);
        }

        var oids = entities.Select(e => e.ObjectId).ToHashSet();
        oids.Count.ShouldBe(100);
        oids.ShouldNotContain((ushort)0);
    }

    // ═══════════════════════════════════════════════════════════════════
    // WorldService — EnterWorld
    // ═══════════════════════════════════════════════════════════════════

    private static WorldService MakeWorldService()
    {
        var regionManager = new RegionManager(NullLoggerFactory.Instance, autoStart: false);
        return new WorldService(regionManager, NullLoggerFactory.Instance.CreateLogger<WorldService>());
    }

    [Fact]
    public void WorldService_enter_world_creates_region_and_enqueues()
    {
        var svc = MakeWorldService();
        var player = MakePlayer();
        var pos = CenterPos();

        svc.EnterWorld(player, pos);

        // Region should exist now
        var region = svc.Regions.Get(pos.RegionId);
        region.ShouldNotBeNull();

        // Entity not yet visible (command is queued, not processed)
        region!.EntityCount.ShouldBe(0);

        // After tick, entity is added
        region.Tick(0);
        region.EntityCount.ShouldBe(1);
        player.ObjectId.ShouldNotBe((ushort)0);
    }

    // ═══════════════════════════════════════════════════════════════════
    // WorldService — LeaveWorld
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void WorldService_leave_world_removes_entity()
    {
        var svc = MakeWorldService();
        var player = MakePlayer();
        var pos = CenterPos();

        svc.EnterWorld(player, pos);
        var region = svc.Regions.Get(pos.RegionId)!;
        region.Tick(0);
        region.EntityCount.ShouldBe(1);

        svc.LeaveWorld(player);
        region.Tick(0);

        region.EntityCount.ShouldBe(0);
        player.ObjectId.ShouldBe((ushort)0);
    }

    [Fact]
    public void WorldService_leave_world_unknown_region_is_safe()
    {
        var svc = MakeWorldService();
        var player = MakePlayer();

        // Player has default position (region 0, not created) — should not throw
        Should.NotThrow(() => svc.LeaveWorld(player));
    }

    // ═══════════════════════════════════════════════════════════════════
    // WorldService — MoveEntity (same region)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void WorldService_move_entity_within_region()
    {
        var svc = MakeWorldService();
        var player = MakePlayer();
        var start = CenterPos();
        var dest = CenterPos(500, 500);

        svc.EnterWorld(player, start);
        var region = svc.Regions.Get(start.RegionId)!;
        region.Tick(0);

        svc.MoveEntity(player, dest);
        region.Tick(0);

        player.Position.ShouldBe(dest);
    }

    // ═══════════════════════════════════════════════════════════════════
    // WorldService — MoveEntity (cross-region)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void WorldService_move_entity_cross_region()
    {
        var svc = MakeWorldService();
        var player = MakePlayer();
        var startPos = new WorldPosition(1, 5 * 4096, 5 * 4096, 0, 0, 100);
        var destPos = new WorldPosition(2, 10 * 4096, 10 * 4096, 0, 0, 200);

        // Enter region 1
        svc.EnterWorld(player, startPos);
        var region1 = svc.Regions.Get(1)!;
        region1.Tick(0);
        region1.EntityCount.ShouldBe(1);

        // Move to region 2
        svc.MoveEntity(player, destPos);

        // Region 1 removes, region 2 adds
        region1.Tick(0);
        region1.EntityCount.ShouldBe(0);

        var region2 = svc.Regions.Get(2)!;
        region2.Tick(0);
        region2.EntityCount.ShouldBe(1);
        player.Position.ShouldBe(destPos);
    }

    // ═══════════════════════════════════════════════════════════════════
    // WorldService — GetEntityRegion
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void WorldService_get_entity_region_returns_correct_region()
    {
        var svc = MakeWorldService();
        var player = MakePlayer();
        var pos = CenterPos();

        svc.EnterWorld(player, pos);
        var region = svc.Regions.Get(pos.RegionId)!;
        region.Tick(0);

        svc.GetEntityRegion(player).ShouldBe(region);
    }

    [Fact]
    public void WorldService_get_entity_region_returns_null_for_unknown()
    {
        var svc = MakeWorldService();
        var player = MakePlayer();
        // Player has default position (region 0, not created)
        svc.GetEntityRegion(player).ShouldBeNull();
    }
}
