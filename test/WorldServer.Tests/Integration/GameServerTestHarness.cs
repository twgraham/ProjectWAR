using System.Net;
using Core.Infrastructure.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WorldServerV2.Network;
using WorldServerV2.Services;

namespace WorldServer.Tests.Integration;

/// <summary>
/// Spins up a real <see cref="NetworkManager"/> on an ephemeral port with full DI
/// (framer, serializer, dispatcher, session registry, player service, lifecycle service).
/// Tests can connect real <see cref="GameClientSimulator"/> instances against it.
/// <para>
/// Disposal tears down the listener, all connections, and the DI container.
/// </para>
/// </summary>
internal sealed class GameServerTestHarness : IAsyncDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly NetworkManager _networkManager;

    /// <summary>The endpoint the server is listening on.</summary>
    public IPEndPoint Endpoint { get; }

    /// <summary>The singleton <see cref="SessionRegistry"/>.</summary>
    public SessionRegistry SessionRegistry { get; }

    /// <summary>The singleton <see cref="PlayerService"/>.</summary>
    public PlayerService PlayerService { get; }

    /// <summary>The test packet dispatcher, so tests can subscribe to dispatch events.</summary>
    public TestPacketDispatcher Dispatcher { get; }

    /// <summary>The underlying DI container.</summary>
    public IServiceProvider Services => _serviceProvider;

    private GameServerTestHarness(
        ServiceProvider serviceProvider,
        NetworkManager networkManager,
        IPEndPoint endpoint,
        SessionRegistry sessionRegistry,
        PlayerService playerService,
        TestPacketDispatcher dispatcher)
    {
        _serviceProvider = serviceProvider;
        _networkManager = networkManager;
        Endpoint = endpoint;
        SessionRegistry = sessionRegistry;
        PlayerService = playerService;
        Dispatcher = dispatcher;
    }

    /// <summary>
    /// Creates and starts a new test harness. The server will be listening for connections
    /// immediately upon return.
    /// </summary>
    public static async Task<GameServerTestHarness> StartAsync()
    {
        var dispatcher = new TestPacketDispatcher();

        // Use port 0 to let the OS assign an ephemeral port.
        var endpoint = new IPEndPoint(IPAddress.Loopback, 0);

        var services = new ServiceCollection();

        services.AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Debug));

        // Core networking — registers NetworkManager as singleton + IHostedService
        services.AddServerNetworking(endpoint)
            .WithPacketFramer<GameServerFramer>(ServiceLifetime.Scoped)
            .WithPacketSerializer<GameServerSerializer>(ServiceLifetime.Scoped)
            .WithPacketDispatcher(dispatcher);

        // Session + player services + lifecycle hosted service
        services.AddGameSessions();

        var serviceProvider = services.BuildServiceProvider();

        var networkManager = serviceProvider.GetRequiredService<NetworkManager>();

        // Start the listener (IHostedService.StartAsync)
        await networkManager.StartAsync(CancellationToken.None);

        // Resolve the actual listening endpoint (with the OS-assigned port).
        // NetworkManager stores the TcpListener in a private field; use reflection to get the assigned port.
        var listenerField = typeof(NetworkManager)
            .GetField("_listener", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var listener = (System.Net.Sockets.TcpListener)listenerField.GetValue(networkManager)!;
        var localEndpoint = (IPEndPoint)listener.LocalEndpoint;

        // Start lifecycle service (subscribes to connect/disconnect events)
        var lifecycleServices = serviceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
        foreach (var hostedService in lifecycleServices)
        {
            if (hostedService is not NetworkManager) // NetworkManager already started above
                await hostedService.StartAsync(CancellationToken.None);
        }

        return new GameServerTestHarness(
            serviceProvider,
            networkManager,
            localEndpoint,
            serviceProvider.GetRequiredService<SessionRegistry>(),
            serviceProvider.GetRequiredService<PlayerService>(),
            dispatcher);
    }

    /// <summary>
    /// Creates a connected <see cref="GameClientSimulator"/>.
    /// </summary>
    public async Task<GameClientSimulator> ConnectClientAsync(CancellationToken ct = default)
    {
        var client = new GameClientSimulator();
        await client.ConnectAsync(Endpoint, ct);
        return client;
    }

    /// <summary>
    /// Waits until the server reports a specific number of connected clients,
    /// or times out after the specified duration.
    /// </summary>
    public async Task WaitForClientCountAsync(int expected, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (_networkManager.ClientCount != expected && Environment.TickCount64 < deadline)
        {
            await Task.Delay(25);
        }
    }

    /// <summary>
    /// Waits until a session is created for a connection, returning the session count.
    /// </summary>
    public async Task<int> WaitForSessionCountAsync(int expected, int timeoutMs = 3000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        int count;
        do
        {
            count = SessionRegistry.Count;
            if (count == expected) return count;
            await Task.Delay(25);
        } while (Environment.TickCount64 < deadline);

        return count;
    }

    public async ValueTask DisposeAsync()
    {
        await _networkManager.StopAsync(CancellationToken.None);
        await _serviceProvider.DisposeAsync();
    }
}
