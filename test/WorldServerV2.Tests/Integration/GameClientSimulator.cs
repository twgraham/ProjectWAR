using System;
using System.Buffers;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using WorldServerV2.Network;

namespace WorldServerV2.Tests.Integration;

/// <summary>
/// A lightweight TCP client that speaks the WAR game server wire protocol.
/// Used in integration tests to simulate a real game client connecting to a
/// <see cref="Core.Infrastructure.Network.NetworkManager"/> instance.
/// <para>
/// Sends packets in the incoming wire format:
/// <c>[uint16 BE packetSize][8-byte header][payload][uint16 BE checksum]</c>
/// <br/>
/// Reads responses in the outgoing wire format:
/// <c>[uint16 BE payloadSize][opcode][payload]</c>
/// </para>
/// </summary>
internal sealed class GameClientSimulator : IAsyncDisposable
{
    private readonly TcpClient _tcp;
    private NetworkStream? _stream;
    private readonly byte[] _readBuffer = new byte[65536];

    public GameClientSimulator()
    {
        _tcp = new TcpClient { NoDelay = true };
    }

    public async Task ConnectAsync(IPEndPoint endpoint, CancellationToken ct = default)
    {
        await _tcp.ConnectAsync(endpoint, ct);
        _stream = _tcp.GetStream();
    }

    /// <summary>
    /// Sends a raw packet in the WAR client→server wire format (unencrypted).
    /// </summary>
    public async Task SendPacketAsync(
        byte opcode,
        ReadOnlyMemory<byte> payload,
        ushort sequenceId = 0,
        ushort sessionId = 0,
        ushort unk1 = 0,
        byte unk2 = 0,
        CancellationToken ct = default)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected.");

        // packetSize = payload.Length (protocol: payloadLen = packetSize + 2, but checksum accounts for +2)
        var packetSize = (ushort)payload.Length;
        var totalLength = 2 + 8 + payload.Length + 2; // sizePrefix + header + payload + checksum
        var buffer = new byte[totalLength];

        var offset = 0;

        // Size prefix (uint16 BE)
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset, 2), packetSize);
        offset += 2;

        // 8-byte header
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset, 2), sequenceId);
        offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset, 2), sessionId);
        offset += 2;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(offset, 2), unk1);
        offset += 2;
        buffer[offset++] = unk2;
        buffer[offset++] = opcode;

        // Payload
        payload.Span.CopyTo(buffer.AsSpan(offset));

        // Checksum over everything except the last 2 bytes
        var checksumSpan = buffer.AsSpan()[..^2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan()[^2..], checksumSpan.ComputeChecksum());

        await _stream.WriteAsync(buffer, ct);
        await _stream.FlushAsync(ct);
    }

    /// <summary>
    /// Reads the next server→client response packet.
    /// Returns the opcode and payload (without the size prefix).
    /// </summary>
    public async Task<(byte Opcode, byte[] Payload)> ReadResponseAsync(
        CancellationToken ct = default,
        int timeoutMs = 5000)
    {
        if (_stream is null) throw new InvalidOperationException("Not connected.");

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeoutMs);

        // Read size prefix (2 bytes, big-endian)
        await ReadExactlyAsync(_stream, _readBuffer, 0, 2, cts.Token);
        var payloadSize = BinaryPrimitives.ReadUInt16BigEndian(_readBuffer.AsSpan(0, 2));

        // Read opcode (1 byte) + payload
        var remaining = 1 + payloadSize;
        await ReadExactlyAsync(_stream, _readBuffer, 0, remaining, cts.Token);

        var opcode = _readBuffer[0];
        var payload = new byte[payloadSize];
        Buffer.BlockCopy(_readBuffer, 1, payload, 0, payloadSize);

        return (opcode, payload);
    }

    /// <summary>
    /// Attempts to read a response, returning null if the timeout expires or the connection closes.
    /// </summary>
    public async Task<(byte Opcode, byte[] Payload)?> TryReadResponseAsync(
        CancellationToken ct = default,
        int timeoutMs = 2000)
    {
        try
        {
            return await ReadResponseAsync(ct, timeoutMs);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    private static async Task ReadExactlyAsync(
        NetworkStream stream, byte[] buffer, int offset, int count, CancellationToken ct)
    {
        var totalRead = 0;
        while (totalRead < count)
        {
            var bytesRead = await stream.ReadAsync(
                buffer.AsMemory(offset + totalRead, count - totalRead), ct);
            if (bytesRead == 0)
                throw new IOException("Connection closed by remote host.");
            totalRead += bytesRead;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_stream is not null)
        {
            try { _stream.Close(); } catch { }
        }

        try { _tcp.Close(); } catch { }
        await Task.CompletedTask;
    }
}
