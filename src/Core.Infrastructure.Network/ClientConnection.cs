using System.Buffers;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Core.Infrastructure.Network;

/// <summary>
/// Manages the TCP transport for a single client connection.
/// Handles I/O loops, packet framing, and dispatches packets to handlers via the generated dispatcher.
/// Implements <see cref="IConnectionContext"/> so handlers can interact with the connection.
/// </summary>
internal sealed class ClientConnection : IConnectionContext, IDisposable
{
    private readonly TcpClient _tcpClient;
    private readonly NetworkStream _stream;
    private readonly byte[] _receiveBuffer;
    private readonly Channel<PacketEnvelope> _receiveQueue;
    private readonly Channel<ReadOnlyMemory<byte>> _sendQueue;
    private readonly IPacketSerializer _serializer;
    private readonly IPacketDispatcher _dispatcher;
    private readonly IServiceScope _connectionScope;
    private readonly ILogger<ClientConnection> _logger;
    private readonly int _errorThreshold;
    private readonly ConcurrentDictionary<string, object> _items = new();

    private Task? _receiveTask;
    private Task? _processTask;
    private Task? _sendTask;
    private CancellationTokenSource? _clientCancellation;
    private int _errorCount;
    private volatile bool _disconnectRequested;
    private DisconnectReason? _pendingDisconnectReason;
    private string? _pendingDisconnectReasonMessage;
    private bool _disposed;

    /// <summary>
    /// Raised when the connection disconnects.
    /// </summary>
    public event Action<DisconnectReason>? Disconnected;

    /// <summary>
    /// Gets the current number of handler errors.
    /// </summary>
    public int ErrorCount => _errorCount;

    // IConnectionContext
    public string? RemoteAddress
    {
        get
        {
            try { return ((IPEndPoint?)_tcpClient.Client.RemoteEndPoint)?.ToString(); }
            catch { return null; }
        }
    }

    public IPacketFramer PacketFramer { get; }

    public IDictionary<string, object> Items => _items;

    public ClientConnection(
        TcpClient tcpClient,
        IPacketFramer framer,
        IPacketSerializer serializer,
        IPacketDispatcher dispatcher,
        IServiceScope connectionScope,
        ILogger<ClientConnection> logger,
        int receiveBufferSize = 65536,
        int errorThreshold = 3)
    {
        _tcpClient = tcpClient ?? throw new ArgumentNullException(nameof(tcpClient));
        _stream = tcpClient.GetStream();
        PacketFramer = framer ?? throw new ArgumentNullException(nameof(framer));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _connectionScope = connectionScope ?? throw new ArgumentNullException(nameof(connectionScope));
        _logger = logger;
        _receiveBuffer = new byte[receiveBufferSize];
        _receiveQueue = Channel.CreateUnbounded<PacketEnvelope>();
        _sendQueue = Channel.CreateUnbounded<ReadOnlyMemory<byte>>();
        _errorThreshold = errorThreshold;
    }

    /// <summary>
    /// Starts the receive, process, and send loops.
    /// </summary>
    public void Start(CancellationToken cancellationToken)
    {
        _clientCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _receiveTask = ReceiveLoopAsync(_clientCancellation.Token);
        _processTask = ProcessLoopAsync(_clientCancellation.Token);
        _sendTask = SendLoopAsync(_clientCancellation.Token);
    }

    public void SendResponse<T>(byte opcode, T response)
    {
        var packet = PacketFramer.CreatePacket(opcode, response, _serializer);
        _sendQueue.Writer.TryWrite(packet);
    }
    
    public void Disconnect(string message, bool flush = false)
    {
        Disconnect(DisconnectReason.ServerInitiated, message, flush: flush);
    }

    public void Disconnect(DisconnectReason reason, string? message = null, bool flush = false)
    {
        if (_disposed) return;

        if (flush)
        {
            if (_disconnectRequested) return; // already flushing
            
            _logger.LogInformation("Disconnect request received. Will flush queued packets beforehand. Reason: {Reason}, Message: {Message}", reason, message);
            
            _disconnectRequested = true;
            _pendingDisconnectReason = reason;
            _pendingDisconnectReasonMessage = message;

            // Stop accepting new packets from the network.
            _receiveQueue.Writer.TryComplete();

            // Enqueue a zero-length sentinel. The send loop will drain all preceding
            // packets, then detect the sentinel and perform the actual teardown.
            _sendQueue.Writer.TryWrite(ReadOnlyMemory<byte>.Empty);
            _sendQueue.Writer.TryComplete();
        }
        else
        {
            // During a graceful flush, suppress non-flush disconnect attempts from
            // other loops (e.g. receive loop hitting ChannelClosedException).
            if (_disconnectRequested) return;
            
            _logger.LogInformation("Disconnecting client connection. Reason: {Reason}, Message: {Message}", reason, message);
            ForceDisconnect(reason);
        }
    }

    /// <summary>
    /// Unconditionally tears down the connection. Used by the send loop after
    /// the disconnect sentinel has been processed.
    /// </summary>
    private void ForceDisconnect(DisconnectReason reason)
    {
        if (_disposed) return;
        _logger.LogInformation("Disconnecting");
        Disconnected?.Invoke(reason);
        Dispose();
    }

    public void OnDispatchError(byte opcode, Exception exception)
    {
        _logger.LogError(exception, "Handler error for opcode 0x{Opcode:X2}", opcode);
        var errors = Interlocked.Increment(ref _errorCount);
        if (errors >= _errorThreshold)
            Disconnect(DisconnectReason.TooManyErrors);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            var bufferOffset = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = await _stream.ReadAsync(
                        _receiveBuffer.AsMemory(bufferOffset, _receiveBuffer.Length - bufferOffset),
                        cancellationToken)
                    .ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    Disconnect(DisconnectReason.ClientDisconnected);
                    return;
                }

                bufferOffset += bytesRead;

                // Extract and queue packets
                bufferOffset = await ExtractAndQueuePacketsAsync(bufferOffset, cancellationToken)
                    .ConfigureAwait(false);

                if (bufferOffset >= _receiveBuffer.Length)
                {
                    Disconnect(DisconnectReason.BufferOverrun);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown via cancellation token - exit cleanly
            Disconnect(DisconnectReason.ServerShutdown);
        }
        catch (SocketException)
        {
            Disconnect(DisconnectReason.SocketError);
        }
        catch (Exception)
        {
            Disconnect(DisconnectReason.SocketError);
        }
    }

    private async Task<int> ExtractAndQueuePacketsAsync(int bufferLength, CancellationToken cancellationToken)
    {
        var buffer = new Memory<byte>(_receiveBuffer, 0, bufferLength);

        if (_logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Buffer hex: {Buffer}", Convert.ToHexString(buffer.Span));

        while (PacketFramer.TryExtractPacket(ref buffer, out var packetData))
        {
            var opcode = PacketFramer.ExtractOpcode(packetData.Span, out var payloadOffset);
            var payloadSlice = packetData[payloadOffset..];

            if (_logger.IsEnabled(LogLevel.Trace))
                _logger.LogTrace("Received packet with opcode 0x{Opcode:X2} and payload size {PayloadLength} bytes", opcode, payloadSlice.Length);

            // Copy payload into pooled memory — the slice points into _receiveBuffer
            // which may be overwritten by a subsequent read before ProcessLoopAsync consumes it.
            var owner = MemoryPool<byte>.Shared.Rent(payloadSlice.Length);
            payloadSlice.Span.CopyTo(owner.Memory.Span);

            await _receiveQueue.Writer.WriteAsync(new PacketEnvelope(opcode, owner, payloadSlice.Length), cancellationToken)
                .ConfigureAwait(false);
        }

        // Compact buffer
        var remaining = buffer.Length;
        var totalConsumed = bufferLength - remaining;
        if (remaining > 0 && totalConsumed > 0)
        {
            Buffer.BlockCopy(_receiveBuffer, totalConsumed, _receiveBuffer, 0, remaining);
        }

        return remaining;
    }

    private async Task ProcessLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var envelope in _receiveQueue.Reader.ReadAllAsync(cancellationToken))
            {
                // When a graceful disconnect is in progress, drain without dispatching
                // so the send loop can flush remaining packets undisturbed.
                if (_disconnectRequested)
                {
                    envelope.Dispose();
                    continue;
                }

                try
                {
                    _dispatcher.Dispatch(
                        envelope.Opcode,
                        envelope.Payload,
                        _connectionScope.ServiceProvider,
                        _serializer,
                        this);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogError(ex, "Deserialization error for opcode 0x{Opcode:X2}", envelope.Opcode);
                    Disconnect(DisconnectReason.MalformedPacket);
                    return;
                }
                catch (Exception ex)
                {
                    OnDispatchError(envelope.Opcode, ex);
                }
                finally
                {
                    envelope.Dispose();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation requested
        }
    }

    private async Task SendLoopAsync(CancellationToken cancellationToken)
    {
        const int maxWritesBeforeFlush = 32;

        try
        {
            var writesSinceFlush = 0;

            await foreach (var data in _sendQueue.Reader.ReadAllAsync(cancellationToken))
            {
                // A zero-length memory is the disconnect sentinel written by
                // Disconnect(reason, flush: true). Flush any buffered data, then
                // tear down the connection.
                if (data.Length == 0)
                {
                    if (writesSinceFlush > 0)
                        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);

                    var disconnectReason = _pendingDisconnectReason ?? DisconnectReason.ServerInitiated;

                    if (_pendingDisconnectReason == null)
                    {
                        _logger.LogError("Flush disconnect triggered without pending reason, defaulting to ServerInitiated");
                    }
                    
                    ForceDisconnect(disconnectReason);
                    return;
                }

                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Sending packet hex: {SendBuffer}", Convert.ToHexString(data.Span));

                try
                {
                    await _stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
                    writesSinceFlush++;

                    // Flush when the queue is momentarily empty or after a batch limit
                    // to prevent data from sitting unflushed under sustained load.
                    if (_sendQueue.Reader.Count == 0 || writesSinceFlush >= maxWritesBeforeFlush)
                    {
                        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                        writesSinceFlush = 0;
                    }
                }
                catch (IOException)
                {
                    Disconnect(DisconnectReason.SocketError);
                    return;
                }
                catch (SocketException)
                {
                    Disconnect(DisconnectReason.SocketError);
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation requested
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _receiveQueue.Writer.TryComplete();
        _sendQueue.Writer.TryComplete();
        _clientCancellation?.Cancel();

        try
        {
            Task.WaitAll(
                new[] { _receiveTask!, _processTask!, _sendTask! }.Where(t => t != null).ToArray(),
                TimeSpan.FromSeconds(5));
        }
        catch { /* Ignore wait errors */ }

        try { _stream?.Close(); } catch { }
        try { _tcpClient?.Close(); } catch { }

        _clientCancellation?.Dispose();

        try { _connectionScope.Dispose(); } catch { }
    }
}
