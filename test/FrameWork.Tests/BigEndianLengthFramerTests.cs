using System;
using System.Buffers;
using System.Buffers.Binary;
using FrameWork.NetWork.V4;
using Shouldly;

namespace FrameWork.Tests;

public class BigEndianLengthFramerTests
{
    private readonly BigEndianLengthFramer _framer = new();

    // ──────────────────────────────────────────────
    // TryExtractPacket
    // ──────────────────────────────────────────────

    [Fact]
    public void TryExtractPacket_SinglePacketWithPayload_ExtractsCorrectly()
    {
        // length header (BE int32) + opcode + payload
        // The length header value represents payload-length in the
        // framer's convention. Let's examine the actual code:
        //   packetLength = ReadInt32BigEndian(buffer[..4])
        //   packet = buffer.Slice(4, packetLength - 4 + 1)
        //   buffer = buffer[(packetLength + 1)..]
        // So packetLength encodes: (total bytes after header - 1 for opcode) + LengthSize
        // Actually: packet = buffer.Slice(LengthSize, packetLength - LengthSize + OpcodeSize)
        //         = buffer.Slice(4, packetLength - 4 + 1) = buffer.Slice(4, packetLength - 3)
        // And buffer advances: buffer[(packetLength + OpcodeSize)..] = buffer[(packetLength + 1)..]
        //
        // For 3 payload bytes + 1 opcode:
        //   We want packet length = 4 bytes (opcode + 3 payload)
        //   packetLength - 3 = 4 → packetLength = 7
        //   buffer advance: 7 + 1 = 8 total bytes consumed from the 4-byte header offset
        //   total wire = 4 (header) + 8 = 12... let me trace more carefully.
        //
        // Actually re-reading the code:
        //   packet = buffer.Slice(LengthSize, packetLength - LengthSize + OpcodeSize)
        //   buffer = buffer[(packetLength + OpcodeSize)..]
        //
        // Check: buffer.Length >= OpcodeSize + packetLength required
        // For completeness let's construct with the CreatePacket method and verify extraction:
        var serializer = new BinaryPacketSerializer();
        var payload = new TestPayload { Id = 42 };

        var created = _framer.CreatePacket((byte)0x10, payload, serializer);
        var buffer = created;

        _framer.TryExtractPacket(ref buffer, out var packet).ShouldBeTrue();

        var opcode = _framer.ExtractOpcode(packet.Span, out var offset);
        opcode.ShouldBe((byte)0x10);

        var data = serializer.Deserialize<TestPayload>(packet[offset..].Span);
        data.Id.ShouldBe((byte)42);
        buffer.Length.ShouldBe(0);
    }

    [Fact]
    public void TryExtractPacket_EmptyBuffer_ReturnsFalse()
    {
        var buffer = ReadOnlyMemory<byte>.Empty;

        _framer.TryExtractPacket(ref buffer, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryExtractPacket_IncompleteLengthHeader_ReturnsFalse()
    {
        // Less than 4 bytes — can't even read the length
        var buffer = new ReadOnlyMemory<byte>(new byte[] { 0x00, 0x00, 0x05 });

        _framer.TryExtractPacket(ref buffer, out _).ShouldBeFalse();
        buffer.Length.ShouldBe(3); // unchanged
    }

    [Fact]
    public void TryExtractPacket_ZeroLengthFrame_IsSkipped()
    {
        // A zero-length frame followed by a real packet
        // Zero-length: [0x00, 0x00, 0x00, 0x00]
        // Real packet: use CreatePacket
        var serializer = new BinaryPacketSerializer();
        var payload = new TestPayload { Id = 99 };
        var realPacket = _framer.CreatePacket((byte)0x20, payload, serializer);

        var combined = new byte[4 + realPacket.Length];
        // Zero-length header
        combined[0] = 0; combined[1] = 0; combined[2] = 0; combined[3] = 0;
        realPacket.CopyTo(combined.AsMemory(4));

        var buffer = new ReadOnlyMemory<byte>(combined);

        // Should skip the zero-length frame and extract the real packet
        _framer.TryExtractPacket(ref buffer, out var packet).ShouldBeTrue();

        var opcode = _framer.ExtractOpcode(packet.Span, out var offset);
        opcode.ShouldBe((byte)0x20);
    }

    [Fact]
    public void TryExtractPacket_NegativeLengthHeader_ReturnsFalse()
    {
        // A negative int32 in big-endian
        var data = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(data, -1);
        data[4] = 0x01; // opcode
        data[5] = 0x02; data[6] = 0x03; data[7] = 0x04;

        var buffer = new ReadOnlyMemory<byte>(data);

        _framer.TryExtractPacket(ref buffer, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryExtractPacket_InsufficientDataForDeclaredLength_ReturnsFalse()
    {
        // Length header says 100 bytes but only a few are present
        var data = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(data, 100);
        data[4] = 0x01; // opcode

        var buffer = new ReadOnlyMemory<byte>(data);

        _framer.TryExtractPacket(ref buffer, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryExtractPacket_MultipleZeroLengthFramesBeforeRealPacket()
    {
        var serializer = new BinaryPacketSerializer();
        var payload = new TestPayload { Id = 77 };
        var realPacket = _framer.CreatePacket((byte)0x30, payload, serializer);

        // 3 zero-length frames, then real packet
        var combined = new byte[12 + realPacket.Length];
        // three zero frames (4 bytes each)
        realPacket.CopyTo(combined.AsMemory(12));

        var buffer = new ReadOnlyMemory<byte>(combined);

        _framer.TryExtractPacket(ref buffer, out var packet).ShouldBeTrue();
        var opcode = _framer.ExtractOpcode(packet.Span, out _);
        opcode.ShouldBe((byte)0x30);
    }

    // ──────────────────────────────────────────────
    // ExtractOpcode
    // ──────────────────────────────────────────────

    [Fact]
    public void ExtractOpcode_ReturnsFirstByte()
    {
        var packet = new byte[] { 0x99, 0xAA, 0xBB };

        var opcode = _framer.ExtractOpcode(packet, out var offset);

        opcode.ShouldBe((byte)0x99);
        offset.ShouldBe(1);
    }

    // ──────────────────────────────────────────────
    // CreatePacket roundtrip
    // ──────────────────────────────────────────────

    [Fact]
    public void CreatePacket_RoundTrips_WithMultipleFields()
    {
        var serializer = new BinaryPacketSerializer();
        var original = new MultiFieldPayload { A = 0x1234, B = 0xAB };

        var packetBytes = _framer.CreatePacket((byte)0x55, original, serializer);
        var buffer = packetBytes;

        _framer.TryExtractPacket(ref buffer, out var extracted).ShouldBeTrue();

        var opcode = _framer.ExtractOpcode(extracted.Span, out var offset);
        opcode.ShouldBe((byte)0x55);

        var deserialized = serializer.Deserialize<MultiFieldPayload>(extracted[offset..].Span);
        deserialized.A.ShouldBe((ushort)0x1234);
        deserialized.B.ShouldBe((byte)0xAB);
    }

    [Fact]
    public void CreatePacket_EmptyPayload_ProducesValidPacket()
    {
        var serializer = new BinaryPacketSerializer();
        var original = new EmptyPayload();

        var packetBytes = _framer.CreatePacket((byte)0x01, original, serializer);
        var buffer = packetBytes;

        _framer.TryExtractPacket(ref buffer, out var extracted).ShouldBeTrue();
        var opcode = _framer.ExtractOpcode(extracted.Span, out _);
        opcode.ShouldBe((byte)0x01);
    }

    // ──────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────

    public class TestPayload { public byte Id { get; set; } }
    public class MultiFieldPayload { public ushort A { get; set; } public byte B { get; set; } }
    public class EmptyPayload { }
}
