using System.Buffers;
using System.Buffers.Binary;

namespace Core.Infrastructure.Network;

/// <summary>
/// Packet framer using a 4-byte big-endian length prefix.
/// Wire format: [int32-BE (length = 4 + payload-length, excludes opcode)][opcode byte][payload bytes]
/// </summary>
public sealed class BigEndianLengthFramer : IPacketFramer
{
    private const int LengthSize = sizeof(int);
    private const int OpcodeSize = sizeof(byte);

    private readonly ArrayBufferWriter<byte> _payloadWriter = new(256);

    public bool TryExtractPacket(ref ReadOnlyMemory<byte> buffer, out ReadOnlyMemory<byte> packet)
    {
        packet = default;

        while (buffer.Length >= LengthSize)
        {
            var packetLength = BinaryPrimitives.ReadInt32BigEndian(buffer[..LengthSize].Span);

            if (packetLength == 0)
            {
                // Skip zero-length frames
                buffer = buffer[LengthSize..];
                continue;
            }

            if (packetLength < 0 || buffer.Length < OpcodeSize + packetLength)
                return false;

            packet = buffer.Slice(LengthSize, packetLength - LengthSize + OpcodeSize);
            buffer = buffer[(packetLength + OpcodeSize)..];
            return true;
        }

        return false;
    }

    public byte ExtractOpcode(ReadOnlySpan<byte> packet, out int payloadOffset)
    {
        payloadOffset = OpcodeSize;
        return packet[0];
    }

    public ReadOnlyMemory<byte> CreatePacket<T>(byte opcode, T payload, IPacketSerializer serializer)
    {
        // Reuse the instance writer — safe because each connection gets its own framer
        _payloadWriter.ResetWrittenCount();
        serializer.Serialize(_payloadWriter, payload);
        var payloadSize = _payloadWriter.WrittenCount;

        // Assemble final packet: [int32 BE length][opcode][payload]
        var packetLength = LengthSize + payloadSize; // length field value = LengthSize + payloadSize, excludes opcode
        var packet = new byte[LengthSize + OpcodeSize + payloadSize];
        BinaryPrimitives.WriteInt32BigEndian(packet.AsSpan(0, LengthSize), packetLength);
        packet[LengthSize] = opcode;
        _payloadWriter.WrittenSpan.CopyTo(packet.AsSpan(LengthSize + OpcodeSize));

        return packet;
    }
}
