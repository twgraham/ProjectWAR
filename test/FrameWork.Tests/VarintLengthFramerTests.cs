using System;
using System.Buffers;
using FrameWork.NetWork.V4;
using Shouldly;

namespace FrameWork.Tests;

public class VarintLengthFramerTests
{
    private readonly VarintLengthFramer _framer = new();

    // ──────────────────────────────────────────────
    // TryExtractPacket
    // ──────────────────────────────────────────────

    [Fact]
    public void TryExtractPacket_SinglePacketWithPayload_ExtractsCorrectly()
    {
        // varint(3) = 0x03, opcode = 0xAA, payload = [1, 2, 3]
        var data = new byte[] { 0x03, 0xAA, 0x01, 0x02, 0x03 };
        var buffer = new ReadOnlyMemory<byte>(data);

        _framer.TryExtractPacket(ref buffer, out var packet).ShouldBeTrue();

        // Packet should be [opcode][payload] = [0xAA, 0x01, 0x02, 0x03]
        packet.Length.ShouldBe(4);
        packet.Span[0].ShouldBe((byte)0xAA);
        packet.Span[1].ShouldBe((byte)0x01);
        packet.Span[2].ShouldBe((byte)0x02);
        packet.Span[3].ShouldBe((byte)0x03);
        buffer.Length.ShouldBe(0);
    }

    [Fact]
    public void TryExtractPacket_ZeroLengthPayload_ExtractsOpcodeOnly()
    {
        // varint(0) = 0x00, opcode = 0xFF
        var data = new byte[] { 0x00, 0xFF };
        var buffer = new ReadOnlyMemory<byte>(data);

        _framer.TryExtractPacket(ref buffer, out var packet).ShouldBeTrue();

        packet.Length.ShouldBe(1);
        packet.Span[0].ShouldBe((byte)0xFF);
        buffer.Length.ShouldBe(0);
    }

    [Fact]
    public void TryExtractPacket_EmptyBuffer_ReturnsFalse()
    {
        var buffer = ReadOnlyMemory<byte>.Empty;

        _framer.TryExtractPacket(ref buffer, out var packet).ShouldBeFalse();
        packet.Length.ShouldBe(0);
    }

    [Fact]
    public void TryExtractPacket_SingleByte_ReturnsFalse()
    {
        // Just a varint byte, no opcode yet
        var buffer = new ReadOnlyMemory<byte>(new byte[] { 0x05 });

        _framer.TryExtractPacket(ref buffer, out _).ShouldBeFalse();
        // Buffer should be restored to original
        buffer.Length.ShouldBe(1);
    }

    [Fact]
    public void TryExtractPacket_IncompletePayload_ReturnsFalseAndRestoresBuffer()
    {
        // varint(5) = 0x05, opcode = 0x01, only 2 of 5 payload bytes
        var data = new byte[] { 0x05, 0x01, 0xAA, 0xBB };
        var buffer = new ReadOnlyMemory<byte>(data);

        _framer.TryExtractPacket(ref buffer, out _).ShouldBeFalse();
        // Buffer should be unchanged
        buffer.Length.ShouldBe(4);
    }

    [Fact]
    public void TryExtractPacket_MultiByteVarint_ExtractsCorrectly()
    {
        // Varint for 200: 0xC8 0x01 (200 = 0x80 | 72 = 0xC8, then 0x01)
        // 200 = 0b11001000 → varint: 0b(1)(1001000) 0b(0)(0000001) = 0xC8 0x01
        var payloadSize = 200;
        var payload = new byte[payloadSize];
        for (var i = 0; i < payloadSize; i++) payload[i] = (byte)(i & 0xFF);

        var data = new byte[2 + 1 + payloadSize]; // varint(2 bytes) + opcode + payload
        data[0] = 0xC8;
        data[1] = 0x01;
        data[2] = 0x42; // opcode
        Array.Copy(payload, 0, data, 3, payloadSize);
        var buffer = new ReadOnlyMemory<byte>(data);

        _framer.TryExtractPacket(ref buffer, out var packet).ShouldBeTrue();
        packet.Length.ShouldBe(1 + payloadSize); // opcode + payload
        packet.Span[0].ShouldBe((byte)0x42);
        buffer.Length.ShouldBe(0);
    }

    [Fact]
    public void TryExtractPacket_MultiplePacketsInBuffer_ExtractsOneAtATime()
    {
        // Two packets: varint(1) opcode payload | varint(2) opcode payload payload
        var data = new byte[] { 0x01, 0xAA, 0x11, 0x02, 0xBB, 0x22, 0x33 };
        var buffer = new ReadOnlyMemory<byte>(data);

        // First packet
        _framer.TryExtractPacket(ref buffer, out var packet1).ShouldBeTrue();
        packet1.Length.ShouldBe(2); // opcode + 1 byte payload
        packet1.Span[0].ShouldBe((byte)0xAA);
        packet1.Span[1].ShouldBe((byte)0x11);

        // Second packet
        _framer.TryExtractPacket(ref buffer, out var packet2).ShouldBeTrue();
        packet2.Length.ShouldBe(3); // opcode + 2 byte payload
        packet2.Span[0].ShouldBe((byte)0xBB);
        buffer.Length.ShouldBe(0);
    }

    [Fact]
    public void TryExtractPacket_ZeroLengthMissingOpcode_ReturnsFalse()
    {
        // varint(0) but no opcode byte follows
        var buffer = new ReadOnlyMemory<byte>(new byte[] { 0x00 });

        _framer.TryExtractPacket(ref buffer, out _).ShouldBeFalse();
    }

    [Fact]
    public void TryExtractPacket_NegativeVarintOverflow_ReturnsFalse()
    {
        // Craft a varint that decodes to a negative number (overflow through bit shifting)
        // 5 continuation bytes with high bits: forces large left shifts that overflow int
        var data = new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x7F, 0x01, 0x00 };
        var buffer = new ReadOnlyMemory<byte>(data);

        // The varint decodes to a very large (or negative) number
        // packetLength < 0 guard should prevent extraction
        _framer.TryExtractPacket(ref buffer, out _).ShouldBeFalse();
    }

    // ──────────────────────────────────────────────
    // ExtractOpcode
    // ──────────────────────────────────────────────

    [Fact]
    public void ExtractOpcode_ReturnsFirstByte()
    {
        var packet = new byte[] { 0x42, 0x01, 0x02 };

        var opcode = _framer.ExtractOpcode(packet, out var payloadOffset);

        opcode.ShouldBe((byte)0x42);
        payloadOffset.ShouldBe(1);
    }

    [Fact]
    public void ExtractOpcode_MinMaxOpcodes()
    {
        _framer.ExtractOpcode(new byte[] { 0x00 }, out _).ShouldBe((byte)0x00);
        _framer.ExtractOpcode(new byte[] { 0xFF }, out _).ShouldBe((byte)0xFF);
    }

    // ──────────────────────────────────────────────
    // CreatePacket roundtrip
    // ──────────────────────────────────────────────

    [Fact]
    public void CreatePacket_RoundTrips_WithTryExtractPacket()
    {
        var serializer = new BinaryPacketSerializer();
        var original = new SimplePayload { Value = 0x1234 };

        var packetBytes = _framer.CreatePacket((byte)0x10, original, serializer);
        var buffer = packetBytes;

        _framer.TryExtractPacket(ref buffer, out var extracted).ShouldBeTrue();

        var opcode = _framer.ExtractOpcode(extracted.Span, out var offset);
        opcode.ShouldBe((byte)0x10);

        var payload = extracted[offset..];
        var deserialized = serializer.Deserialize<SimplePayload>(payload.Span);
        deserialized.Value.ShouldBe((ushort)0x1234);
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
    // Helper types
    // ──────────────────────────────────────────────

    public class SimplePayload
    {
        public ushort Value { get; set; }
    }

    public class EmptyPayload { }
}
