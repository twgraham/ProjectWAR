using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using WorldServerV2.Data.Entities;
using WorldServerV2.Network;
using WorldServerV2.Network.Dtos;
using WorldServerV2.Services;
using WorldServerV2.Services.PlayerInit;
using WorldServerV2.World.Entities;

namespace WorldServer.Tests;

/// <summary>
/// Tests for <see cref="PlayerInitPipeline"/>, <see cref="IPlayerInitStep"/> implementations,
/// and <see cref="PlayerEntity.InitializeFromCharacter"/>.
/// </summary>
public class PlayerInitPipelineTests
{
    // ── Helpers ─────────────────────────────────────────────────────────

    private static readonly ILogger<PlayerInitPipeline> PipelineLogger =
        NullLoggerFactory.Instance.CreateLogger<PlayerInitPipeline>();

    private static Character MakeCharacter(byte level = 20, byte realm = 1, byte career = 3, int speed = 100)
        => new()
        {
            CharacterId = 42,
            Name = "Hero",
            Realm = realm,
            Career = career,
            Value = new CharacterValue
            {
                CharacterId = 42,
                Level = level,
                Speed = speed,
                RegionId = 1,
                ZoneId = 100,
                WorldX = 5000,
                WorldY = 6000,
                WorldZ = 500,
                WorldO = 2048,
            },
        };

    private static PlayerEntity MakePlayer(byte level = 20, byte realm = 1, byte career = 3, int speed = 100)
        => new(0, MakeCharacter(level, realm, career, speed), 1000);

    private static (GameSession Session, RecordingConnectionContext Recorder) MakeSession()
    {
        var recorder = new RecordingConnectionContext();
        var session = new GameSession(0, recorder);
        return (session, recorder);
    }

    // ── PlayerEntity.InitializeFromCharacter ────────────────────────────

    [Fact]
    public void InitializeFromCharacter_sets_level_from_character_value()
    {
        var player = MakePlayer(level: 30);

        player.InitializeFromCharacter();

        player.Level.ShouldBe((byte)30);
    }

    [Fact]
    public void InitializeFromCharacter_sets_realm_from_character()
    {
        var player = MakePlayer(realm: 2);

        player.InitializeFromCharacter();

        player.Realm.ShouldBe((byte)2);
    }

    [Fact]
    public void InitializeFromCharacter_sets_faction_to_realm()
    {
        var player = MakePlayer(realm: 1);

        player.InitializeFromCharacter();

        player.Faction.ShouldBe((byte)1);
    }

    [Fact]
    public void InitializeFromCharacter_resurrects_to_full_health()
    {
        var player = MakePlayer();
        // Kill the player first so Resurrect actually works
        player.Health.TakeDamage(player.Health.Max);
        player.Health.IsDead.ShouldBeTrue();

        player.InitializeFromCharacter();

        player.Health.IsAlive.ShouldBeTrue();
        player.Health.Current.ShouldBe(player.Health.Max);
    }

    // ── StatsInitStep ──────────────────────────────────────────────────

    [Fact]
    public void StatsInitStep_uses_zero_based_stat_ids()
    {
        var player = MakePlayer(level: 15);
        player.InitializeFromCharacter();
        var (session, recorder) = MakeSession();
        var step = new StatsInitStep();

        step.Execute(player, session);

        recorder.SentPackets.ShouldHaveSingleItem();
        var (opcode, packet) = recorder.SentPackets[0];
        opcode.ShouldBe((byte)Opcodes.F_PLAYER_STATS);
        var stats = (PlayerStatsResponse)packet;

        // First stat ID should be 0, not 1
        stats.StatEntries[0].ShouldBe((byte)0);
        // Second stat entry starts at offset 3
        stats.StatEntries[3].ShouldBe((byte)1);
    }

    [Fact]
    public void StatsInitStep_tactic_slots_zero_at_level_10()
    {
        var player = MakePlayer(level: 10);
        player.InitializeFromCharacter();
        var (session, recorder) = MakeSession();
        var step = new StatsInitStep();

        step.Execute(player, session);

        var stats = (PlayerStatsResponse)recorder.SentPackets[0].Packet;
        stats.TacticSlots.ShouldBe((byte)0);
    }

    [Fact]
    public void StatsInitStep_tactic_slots_one_at_level_11()
    {
        var player = MakePlayer(level: 11);
        player.InitializeFromCharacter();
        var (session, recorder) = MakeSession();
        var step = new StatsInitStep();

        step.Execute(player, session);

        var stats = (PlayerStatsResponse)recorder.SentPackets[0].Packet;
        stats.TacticSlots.ShouldBe((byte)1);
    }

    [Fact]
    public void StatsInitStep_tactic_slots_four_at_level_40()
    {
        var player = MakePlayer(level: 40);
        player.InitializeFromCharacter();
        var (session, recorder) = MakeSession();
        var step = new StatsInitStep();

        step.Execute(player, session);

        var stats = (PlayerStatsResponse)recorder.SentPackets[0].Packet;
        stats.TacticSlots.ShouldBe((byte)4);
    }

    // ── HealthInitStep ─────────────────────────────────────────────────

    [Fact]
    public void HealthInitStep_sends_current_and_max_hp()
    {
        var player = MakePlayer();
        player.InitializeFromCharacter();
        var (session, recorder) = MakeSession();
        var step = new HealthInitStep();

        step.Execute(player, session);

        recorder.SentPackets.ShouldHaveSingleItem();
        var (opcode, packet) = recorder.SentPackets[0];
        opcode.ShouldBe((byte)Opcodes.F_PLAYER_HEALTH);
        var health = (PlayerHealthResponse)packet;
        health.Health.ShouldBe(1000u);
        health.MaxHealth.ShouldBe(1000u);
    }

    // ── SpeedInitStep ──────────────────────────────────────────────────

    [Fact]
    public void SpeedInitStep_sends_character_speed()
    {
        var player = MakePlayer(speed: 150);
        var (session, recorder) = MakeSession();
        var step = new SpeedInitStep();

        step.Execute(player, session);

        recorder.SentPackets.ShouldHaveSingleItem();
        var speed = (SpeedResponse)recorder.SentPackets[0].Packet;
        speed.Speed.ShouldBe((ushort)150);
        speed.CanMove.ShouldBe((byte)1);
    }

    [Fact]
    public void SpeedInitStep_defaults_to_100_when_speed_is_zero()
    {
        var player = MakePlayer(speed: 0);
        var (session, recorder) = MakeSession();
        var step = new SpeedInitStep();

        step.Execute(player, session);

        var speed = (SpeedResponse)recorder.SentPackets[0].Packet;
        speed.Speed.ShouldBe((ushort)100);
    }

    // ── PlayerInitPipeline ─────────────────────────────────────────────

    [Fact]
    public void Pipeline_calls_InitializeFromCharacter_and_all_steps_in_order()
    {
        var step1 = new RecordingStep("step1");
        var step2 = new RecordingStep("step2");
        var step3 = new RecordingStep("step3");
        var pipeline = new PlayerInitPipeline(PipelineLogger, [step1, step2, step3]);

        var player = MakePlayer(level: 25, realm: 2);
        player.AssignOid(100);
        var (session, _) = MakeSession();

        pipeline.Initialize(player, session);

        // Phase B: entity state should be initialized
        player.Level.ShouldBe((byte)25);
        player.Realm.ShouldBe((byte)2);
        player.Faction.ShouldBe((byte)2);

        // Phase C: all steps should have executed in order
        step1.ExecutedAt.ShouldNotBeNull();
        step2.ExecutedAt.ShouldNotBeNull();
        step3.ExecutedAt.ShouldNotBeNull();
        step1.ExecutedAt!.Value.ShouldBeLessThan(step2.ExecutedAt!.Value);
        step2.ExecutedAt!.Value.ShouldBeLessThan(step3.ExecutedAt!.Value);
    }

    [Fact]
    public void Pipeline_with_no_steps_still_initializes_entity()
    {
        var pipeline = new PlayerInitPipeline(PipelineLogger, []);
        var player = MakePlayer(level: 15, realm: 1);
        player.AssignOid(50);
        var (session, _) = MakeSession();

        pipeline.Initialize(player, session);

        player.Level.ShouldBe((byte)15);
        player.Realm.ShouldBe((byte)1);
    }

    [Fact]
    public void Pipeline_null_player_throws()
    {
        var pipeline = new PlayerInitPipeline(PipelineLogger, []);
        var (session, _) = MakeSession();
        Should.Throw<ArgumentNullException>(() => pipeline.Initialize(null!, session));
    }

    [Fact]
    public void Pipeline_null_session_throws()
    {
        var pipeline = new PlayerInitPipeline(PipelineLogger, []);
        Should.Throw<ArgumentNullException>(() => pipeline.Initialize(MakePlayer(), null!));
    }

    // ── Test Doubles ───────────────────────────────────────────────────

    private static long _orderCounter;

    /// <summary>
    /// A test <see cref="IPlayerInitStep"/> that records when it was executed.
    /// </summary>
    private sealed class RecordingStep(string name) : IPlayerInitStep
    {
        public string Name { get; } = name;
        public long? ExecutedAt { get; private set; }

        public void Execute(PlayerEntity player, GameSession session)
        {
            ExecutedAt = Interlocked.Increment(ref _orderCounter);
        }
    }

    /// <summary>
    /// A fake <see cref="Core.Infrastructure.Network.IConnectionContext"/> that captures
    /// packets sent via <see cref="Core.Infrastructure.Network.IConnectionContext.SendResponse{T}"/>.
    /// </summary>
    private sealed class RecordingConnectionContext : Core.Infrastructure.Network.IConnectionContext
    {
        public string? RemoteAddress => "127.0.0.1";
        public IDictionary<string, object> Items { get; } = new Dictionary<string, object>();
        public Core.Infrastructure.Network.IPacketFramer PacketFramer => null!;

        public List<(byte Opcode, object Packet)> SentPackets { get; } = [];

        public void SendResponse<T>(byte opcode, T response)
        {
            SentPackets.Add((opcode, response!));
        }

        public void Disconnect(string reason, bool flush = false) { }
        public void OnDispatchError(byte opcode, Exception exception) { }
    }
}
