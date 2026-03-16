using Shouldly;
using WorldServerV2.Network;

namespace WorldServer.Tests.Integration;

/// <summary>
/// Integration smoke tests that boot a real <see cref="Core.Infrastructure.Network.NetworkManager"/>
/// with full DI and drive it via raw TCP using <see cref="GameClientSimulator"/>.
/// <para>
/// These tests verify the new V2 networking stack produces correct wire-level behavior
/// without involving any game-world systems (mocked at the dispatcher level).
/// </para>
/// </summary>
public sealed class NetworkSmokeSuite : IAsyncLifetime
{
    private GameServerTestHarness _harness = null!;

    public async ValueTask InitializeAsync()
    {
        _harness = await GameServerTestHarness.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _harness.DisposeAsync();
    }

    #region Connection lifecycle

    [Fact]
    public async Task TcpConnect_CreatesSessionInRegistry()
    {
        // A raw TCP connection should cause SessionLifecycleService to create a GameSession.
        await using var client = await _harness.ConnectClientAsync();

        var sessionCount = await _harness.WaitForSessionCountAsync(1);
        sessionCount.ShouldBe(1);
    }

    [Fact]
    public async Task TcpDisconnect_RemovesSessionFromRegistry()
    {
        await using var client = await _harness.ConnectClientAsync();
        await _harness.WaitForSessionCountAsync(1);

        // Dispose the client (closes TCP connection).
        await client.DisposeAsync();

        // Wait for the server to notice the disconnect and remove the session.
        var sessionCount = await _harness.WaitForSessionCountAsync(0, timeoutMs: 5000);
        sessionCount.ShouldBe(0);
    }

    [Fact]
    public async Task MultipleClients_EachGetsOwnSession()
    {
        await using var client1 = await _harness.ConnectClientAsync();
        await using var client2 = await _harness.ConnectClientAsync();
        await using var client3 = await _harness.ConnectClientAsync();

        var sessionCount = await _harness.WaitForSessionCountAsync(3);
        sessionCount.ShouldBe(3);
    }

    #endregion

    #region F_ENCRYPTKEY handshake

    [Fact]
    public async Task EncryptKey_Cipher0_ReturnsReceiveEncryptKeyWithStatus1()
    {
        await using var client = await _harness.ConnectClientAsync();
        await _harness.WaitForSessionCountAsync(1);

        // Build F_ENCRYPTKEY payload: cipher=0, application=0, major=1, minor=2, rev=3, unk1=0, key=empty
        var payload = new byte[] { 0x00, 0x00, 0x01, 0x02, 0x03, 0x00 };
        await client.SendPacketAsync(opcode: 0x5C, payload: payload);

        // Read the server's response
        var response = await client.ReadResponseAsync(timeoutMs: 5000);

        // Opcode should be F_RECEIVE_ENCRYPTKEY = 0x8A
        response.Opcode.ShouldBe((byte)0x8A);

        // Payload should be a single byte: Status = 1 (no encryption)
        response.Payload.Length.ShouldBe(1);
        response.Payload[0].ShouldBe((byte)1);
    }

    [Fact]
    public async Task EncryptKey_Cipher0_WithVersionInfo_ReturnsCorrectResponse()
    {
        await using var client = await _harness.ConnectClientAsync();
        await _harness.WaitForSessionCountAsync(1);

        // cipher=0, application=1, major=8, minor=0, revision=33, unk1=0
        var payload = new byte[] { 0x00, 0x01, 0x08, 0x00, 0x21, 0x00 };
        await client.SendPacketAsync(opcode: 0x5C, payload: payload);

        var response = await client.ReadResponseAsync(timeoutMs: 5000);

        response.Opcode.ShouldBe((byte)0x8A);
        response.Payload.Length.ShouldBe(1);
        response.Payload[0].ShouldBe((byte)1);
    }

    #endregion

    #region F_DISCONNECT

    [Fact]
    public async Task Disconnect_PacketTriggersServerSideDisconnect()
    {
        await using var client = await _harness.ConnectClientAsync();
        await _harness.WaitForSessionCountAsync(1);

        // Send F_DISCONNECT (opcode 0x10) with empty payload
        await client.SendPacketAsync(opcode: 0x10, payload: ReadOnlyMemory<byte>.Empty);

        // The server should disconnect us — the session should be removed.
        var sessionCount = await _harness.WaitForSessionCountAsync(0, timeoutMs: 5000);
        sessionCount.ShouldBe(0);
    }

    #endregion

    #region Dispatch event observability

    [Fact]
    public async Task Dispatcher_RaisesEventOnPacketReceived()
    {
        var dispatched = new TaskCompletionSource<byte>();
        _harness.Dispatcher.PacketDispatched += (opcode, _) => dispatched.TrySetResult(opcode);

        await using var client = await _harness.ConnectClientAsync();
        await _harness.WaitForSessionCountAsync(1);

        var payload = new byte[] { 0x00, 0x00, 0x01, 0x02, 0x03, 0x00 };
        await client.SendPacketAsync(opcode: 0x5C, payload: payload);

        var receivedOpcode = await dispatched.Task.WaitAsync(TimeSpan.FromSeconds(5));
        receivedOpcode.ShouldBe((byte)0x5C);
    }

    #endregion

    #region Stress / concurrency

    [Fact]
    public async Task ConcurrentConnections_AllSessionsTracked()
    {
        const int clientCount = 20;
        var clients = new List<GameClientSimulator>();

        try
        {
            var connectTasks = Enumerable.Range(0, clientCount)
                .Select(_ => _harness.ConnectClientAsync())
                .ToArray();

            var connected = await Task.WhenAll(connectTasks);
            clients.AddRange(connected);

            var sessionCount = await _harness.WaitForSessionCountAsync(clientCount, timeoutMs: 10000);
            sessionCount.ShouldBe(clientCount);
        }
        finally
        {
            foreach (var c in clients)
                await c.DisposeAsync();
        }
    }

    [Fact]
    public async Task ConcurrentEncryptKeyRequests_AllGetResponses()
    {
        const int clientCount = 10;
        var clients = new List<GameClientSimulator>();

        try
        {
            // Connect all clients first
            var connectTasks = Enumerable.Range(0, clientCount)
                .Select(_ => _harness.ConnectClientAsync())
                .ToArray();
            var connected = await Task.WhenAll(connectTasks);
            clients.AddRange(connected);

            await _harness.WaitForSessionCountAsync(clientCount, timeoutMs: 10000);

            // Send F_ENCRYPTKEY from all clients simultaneously
            var payload = new byte[] { 0x00, 0x00, 0x01, 0x02, 0x03, 0x00 };
            var sendTasks = clients.Select(c => c.SendPacketAsync(0x5C, payload)).ToArray();
            await Task.WhenAll(sendTasks);

            // All should get back F_RECEIVE_ENCRYPTKEY
            var readTasks = clients.Select(c => c.ReadResponseAsync(timeoutMs: 10000)).ToArray();
            var responses = await Task.WhenAll(readTasks);

            foreach (var response in responses)
            {
                response.Opcode.ShouldBe((byte)0x8A);
                response.Payload.Length.ShouldBe(1);
                response.Payload[0].ShouldBe((byte)1);
            }
        }
        finally
        {
            foreach (var c in clients)
                await c.DisposeAsync();
        }
    }

    #endregion
}
